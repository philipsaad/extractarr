using System.Collections.Concurrent;
using System.Text.Json;
using NLog;
using Extractarr.Common.Disk;
using Extractarr.Common.Instrumentation;
using Extractarr.Core.Configuration;
using Extractarr.Core.Instances;
using Extractarr.Core.Messaging.Events;
using Extractarr.Core.Notifications;
using Extractarr.Core.RemotePathMappings;
using Extractarr.Core.WatchFolders;
using Extractarr.SignalR;

namespace Extractarr.Core.Extraction;

public class ArrWebhookPayload
{
    public string? EventType { get; set; } // Grab, Download, Rename, Test, ImportComplete
    public string? InstanceName { get; set; }
    public string? ApplicationUrl { get; set; }
    public string? DownloadId { get; set; }
    public string? DownloadClient { get; set; }
    public ArrWebhookRelease? Release { get; set; }
    public ArrWebhookMovieOrSeries? Series { get; set; }
    public ArrWebhookMovieOrSeries? Movie { get; set; }
    public string? SourcePath { get; set; }
    public string? DestinationPath { get; set; }
}

public class ArrWebhookRelease
{
    public string? ReleaseTitle { get; set; }
}

public class ArrWebhookMovieOrSeries
{
    public string? Title { get; set; }
    public string? Path { get; set; }
}

public interface IExtractionLifecycleCoordinator
{
    Task PollAllEnabledInstancesAsync(CancellationToken cancellationToken = default);
    Task PollSingleInstanceAsync(int instanceId, CancellationToken cancellationToken = default);
    Task ProcessExtractionQueueAsync(CancellationToken cancellationToken = default);
    Task ProcessPendingCleanupsAsync(CancellationToken cancellationToken = default);
    Task HandleWebhookAsync(ArrWebhookPayload payload, CancellationToken cancellationToken = default);
    ExtractionJob? EnqueueFromWatchFolder(WatchFolder folder, string archiveFilePath);
    bool CancelJob(int jobId);
    Task<ExtractionJob?> ForceExtractJobAsync(int jobId, CancellationToken cancellationToken = default);
    Task<ExtractionJob?> RetryJobAsync(int jobId, string? manualPassword = null, CancellationToken cancellationToken = default);
    Task<ExtractionJob?> RetryBlocklistItemAsync(int blocklistId, string? manualPassword = null, CancellationToken cancellationToken = default);
}

public class ExtractionLifecycleCoordinator : IExtractionLifecycleCoordinator
{
    private readonly IArrInstanceRepository _instanceRepo;
    private readonly IArrApiClient _arrApiClient;
    private readonly IRemotePathMappingService _pathMappingService;
    private readonly IExtractionJobRepository _jobRepo;
    private readonly IExtractionHistoryRepository _historyRepo;
    private readonly IBlocklistRepository _blocklistRepo;
    private readonly IWatchFolderRepository _watchFolderRepo;
    private readonly ISharpCompressExtractor _extractor;
    private readonly IConfigService _configService;
    private readonly IDiskProvider _diskProvider;
    private readonly IEventAggregator _eventAggregator;
    private readonly IBroadcastSignalRMessage _signalR;

    private readonly ConcurrentDictionary<int, CancellationTokenSource> _runningExtractions = new();
    private static readonly SemaphoreSlim _queueCoordinatorLock = new(1, 1);
    private static readonly Logger Logger = ExtractarrLogger.GetLogger<ExtractionLifecycleCoordinator>();

    public ExtractionLifecycleCoordinator(
        IArrInstanceRepository instanceRepo,
        IArrApiClient arrApiClient,
        IRemotePathMappingService pathMappingService,
        IExtractionJobRepository jobRepo,
        IExtractionHistoryRepository historyRepo,
        IBlocklistRepository blocklistRepo,
        IWatchFolderRepository watchFolderRepo,
        ISharpCompressExtractor extractor,
        IConfigService configService,
        IDiskProvider diskProvider,
        IEventAggregator eventAggregator,
        IBroadcastSignalRMessage signalR)
    {
        _instanceRepo = instanceRepo;
        _arrApiClient = arrApiClient;
        _pathMappingService = pathMappingService;
        _jobRepo = jobRepo;
        _historyRepo = historyRepo;
        _blocklistRepo = blocklistRepo;
        _watchFolderRepo = watchFolderRepo;
        _extractor = extractor;
        _configService = configService;
        _diskProvider = diskProvider;
        _eventAggregator = eventAggregator;
        _signalR = signalR;
    }

    public async Task PollAllEnabledInstancesAsync(CancellationToken cancellationToken = default)
    {
        var enabled = _instanceRepo.GetEnabled();
        foreach (var instance in enabled)
        {
            await PollInstanceInternalAsync(instance, forceNow: false, cancellationToken);
        }

        await ProcessExtractionQueueAsync(cancellationToken);
        await ProcessPendingCleanupsAsync(cancellationToken);
    }

    public async Task PollSingleInstanceAsync(int instanceId, CancellationToken cancellationToken = default)
    {
        var instance = _instanceRepo.Find(instanceId);
        if (instance == null)
        {
            return;
        }

        await PollInstanceInternalAsync(instance, forceNow: true, cancellationToken);
        await ProcessExtractionQueueAsync(cancellationToken);
        await ProcessPendingCleanupsAsync(cancellationToken);
    }

    private async Task PollInstanceInternalAsync(ArrInstance instance, bool forceNow, CancellationToken cancellationToken)
    {
        if (!forceNow && instance.LastPolledAt.HasValue)
        {
            var elapsed = (DateTime.UtcNow - instance.LastPolledAt.Value).TotalSeconds;
            if (elapsed < Math.Max(5, instance.PollIntervalSeconds))
            {
                return;
            }
        }

        try
        {
            var queueRecords = await _arrApiClient.GetQueueAsync(instance, cancellationToken);
            instance.HealthStatus = "Online";
            instance.LastErrorMessage = null;
            instance.ActiveQueueCount = queueRecords.Count;
            instance.LastPolledAt = DateTime.UtcNow;
            _instanceRepo.Update(instance);
            _signalR.BroadcastMessage("instance", "updated", instance);

            var activeDownloadIdsInArrQueue = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allowedProtocols = instance.GetProtocolsList();

            foreach (var item in queueRecords)
            {
                var downloadId = !string.IsNullOrWhiteSpace(item.DownloadId)
                    ? item.DownloadId!
                    : $"{instance.Name}-{item.Id}";

                activeDownloadIdsInArrQueue.Add(downloadId);

                if (!item.IsCompletedAndReadyForExtraction())
                {
                    continue;
                }

                // Filter by monitored protocols if configured
                if (allowedProtocols.Count > 0 && !string.IsNullOrWhiteSpace(item.Protocol))
                {
                    if (!allowedProtocols.Any(p => p.Equals(item.Protocol, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                }

                if (string.IsNullOrWhiteSpace(item.OutputPath))
                {
                    continue;
                }

                var localPath = _pathMappingService.MapRemoteToLocal(instance.Name, instance.BaseUrl, item.OutputPath);
                if (!File.Exists(localPath) && !Directory.Exists(localPath))
                {
                    continue;
                }

                var archives = _extractor.DiscoverPrimaryArchives(localPath, recursive: true);
                foreach (var archiveFile in archives)
                {
                    // Skip if blocklisted
                    if (_blocklistRepo.FindByDownloadOrArchive(downloadId, archiveFile) != null)
                    {
                        continue;
                    }

                    // Skip if already tracked
                    var existingJob = _jobRepo.FindByArchivePath(archiveFile);
                    if (existingJob != null && existingJob.DownloadId.Equals(downloadId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var config = _configService.GetExtractionConfig();
                    var fileInfo = new FileInfo(archiveFile);
                    var newJob = new ExtractionJob
                    {
                        ArrInstanceId = instance.Id,
                        InstanceName = instance.Name,
                        InstanceType = instance.InstanceType,
                        ExternalQueueId = item.Id,
                        DownloadId = downloadId,
                        ReleaseTitle = !string.IsNullOrWhiteSpace(item.Title) ? item.Title! : Path.GetFileNameWithoutExtension(archiveFile),
                        Protocol = item.Protocol ?? "torrent",
                        RemoteSourcePath = item.OutputPath,
                        LocalSourcePath = File.Exists(localPath) ? (Path.GetDirectoryName(localPath) ?? localPath) : localPath,
                        ArchivePath = archiveFile,
                        ArchiveType = _extractor.DetectArchiveType(archiveFile),
                        Status = nameof(ExtractionStatus.Queued),
                        TotalBytes = fileInfo.Exists ? fileInfo.Length : 0,
                        MaxRetries = config.MaxRetries,
                        DeleteOriginalArchives = instance.DeleteOriginalArchives,
                        SyncthingWait = instance.SyncthingWait,
                        CreatedAt = DateTime.UtcNow
                    };

                    _jobRepo.Insert(newJob);
                    Logger.Info("Queued archive for extraction from {0}: {1} ({2})", instance.Name, newJob.ReleaseTitle, archiveFile);
                    _signalR.BroadcastMessage("queue", "created", newJob);
                }
            }

            // Lifecycle transition 4 -> 5:
            // Any job for this instance currently in ExtractedPendingImport whose DownloadId is NO LONGER in the *arr queue
            // means *arr finished importing it! Transition to ImportedPendingCleanup with DeleteDelay timer.
            var pendingImportJobs = _jobRepo.GetActiveQueue()
                .Where(j => j.ArrInstanceId == instance.Id && j.Status == nameof(ExtractionStatus.ExtractedPendingImport))
                .ToList();

            var extConfig = _configService.GetExtractionConfig();
            foreach (var job in pendingImportJobs)
            {
                if (!activeDownloadIdsInArrQueue.Contains(job.DownloadId))
                {
                    TransitionToImportedPendingCleanup(job, extConfig.DeleteDelaySeconds);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed polling *arr instance {0} ({1})", instance.Name, instance.BaseUrl);
            instance.HealthStatus = "Error";
            instance.LastErrorMessage = ex.Message;
            instance.LastPolledAt = DateTime.UtcNow;
            _instanceRepo.Update(instance);
            _signalR.BroadcastMessage("instance", "updated", instance);
        }
    }

    public void TransitionToImportedPendingCleanup(ExtractionJob job, int deleteDelaySeconds)
    {
        job.Status = nameof(ExtractionStatus.ImportedPendingCleanup);
        job.ImportedAt = DateTime.UtcNow;
        job.CleanupScheduledAt = DateTime.UtcNow.AddSeconds(Math.Max(0, deleteDelaySeconds));
        _jobRepo.Update(job);

        _historyRepo.Insert(new ExtractionHistory
        {
            ExtractionJobId = job.Id,
            ArrInstanceId = job.ArrInstanceId,
            WatchFolderId = job.WatchFolderId,
            InstanceName = job.InstanceName,
            InstanceType = job.InstanceType,
            DownloadId = job.DownloadId,
            ReleaseTitle = job.ReleaseTitle,
            ArchivePath = job.ArchivePath,
            ArchiveType = job.ArchiveType,
            EventType = "Imported",
            Status = nameof(ExtractionStatus.ImportedPendingCleanup),
            ArchiveSizeBytes = job.TotalBytes,
            ExtractedFileCount = job.GetExtractedFiles().Count,
            ExtractedFilesJson = job.ExtractedFilesJson,
            DurationMs = 0,
            Timestamp = DateTime.UtcNow
        });

        Logger.Info("Release '{0}' imported by {1}. Cleanup of extracted files scheduled at {2:O} (in {3}s)",
            job.ReleaseTitle, job.InstanceName, job.CleanupScheduledAt, deleteDelaySeconds);

        _signalR.BroadcastMessage("queue", "updated", job);
        _signalR.BroadcastMessage("history", "created", new { jobId = job.Id });
    }

    public ExtractionJob? EnqueueFromWatchFolder(WatchFolder folder, string archiveFilePath)
    {
        archiveFilePath = OsPathTranslator.TranslateToCurrentOs(archiveFilePath);
        if (!File.Exists(archiveFilePath))
        {
            return null;
        }

        var downloadId = $"watchfolder-{folder.Id}-{Path.GetFileName(archiveFilePath)}";
        if (_blocklistRepo.FindByDownloadOrArchive(downloadId, archiveFilePath) != null)
        {
            return null;
        }

        var existing = _jobRepo.FindByArchivePath(archiveFilePath);
        if (existing != null && existing.Status != nameof(ExtractionStatus.Failed))
        {
            return existing;
        }

        var config = _configService.GetExtractionConfig();
        var targetDir = !string.IsNullOrWhiteSpace(folder.TargetPath)
            ? OsPathTranslator.TranslateToCurrentOs(folder.TargetPath)
            : (Path.GetDirectoryName(archiveFilePath) ?? OsPathTranslator.TranslateToCurrentOs(folder.Path));

        var fileInfo = new FileInfo(archiveFilePath);
        var job = new ExtractionJob
        {
            WatchFolderId = folder.Id,
            InstanceName = folder.Name,
            InstanceType = "WatchFolder",
            DownloadId = downloadId,
            ReleaseTitle = Path.GetFileNameWithoutExtension(archiveFilePath),
            Protocol = "local",
            RemoteSourcePath = archiveFilePath,
            LocalSourcePath = targetDir,
            ArchivePath = archiveFilePath,
            ArchiveType = _extractor.DetectArchiveType(archiveFilePath),
            Status = nameof(ExtractionStatus.Queued),
            TotalBytes = fileInfo.Exists ? fileInfo.Length : 0,
            MaxRetries = config.MaxRetries,
            DeleteOriginalArchives = folder.DeleteOriginalArchives,
            SyncthingWait = folder.SyncthingWait,
            CreatedAt = DateTime.UtcNow
        };

        _jobRepo.Insert(job);
        _signalR.BroadcastMessage("queue", "created", job);
        return job;
    }

    public async Task ProcessExtractionQueueAsync(CancellationToken cancellationToken = default)
    {
        if (!await _queueCoordinatorLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var config = _configService.GetExtractionConfig();
            var maxConcurrent = Math.Max(1, config.MaxConcurrentExtractions);

            var activeJobs = _jobRepo.GetActiveQueue();
            var candidates = activeJobs
                .Where(j => j.Status is nameof(ExtractionStatus.Queued) or nameof(ExtractionStatus.Waiting))
                .OrderBy(j => j.CreatedAt)
                .ToList();

            var tasks = new List<Task>();

            foreach (var job in candidates)
            {
                if (_runningExtractions.Count >= maxConcurrent)
                {
                    break;
                }

                // Check Waiting conditions: file lock or Syncthing .tmp files
                var sourceDir = Path.GetDirectoryName(job.ArchivePath) ?? job.LocalSourcePath;
                var isLocked = _diskProvider.IsFileLocked(job.ArchivePath);
                var hasSyncthingTmp = job.SyncthingWait && _diskProvider.HasSyncthingTempFiles(sourceDir);

                if (isLocked || hasSyncthingTmp)
                {
                    if (job.Status != nameof(ExtractionStatus.Waiting))
                    {
                        job.Status = nameof(ExtractionStatus.Waiting);
                        job.ErrorMessage = isLocked
                            ? "Waiting for archive file lock to release..."
                            : "Waiting for Syncthing synchronization (.tmp files) to complete...";
                        _jobRepo.Update(job);
                        _signalR.BroadcastMessage("queue", "updated", job);
                    }
                    continue;
                }

                tasks.Add(ExecuteJobExtractionAsync(job, cancellationToken));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }
        finally
        {
            _queueCoordinatorLock.Release();
        }
    }

    private async Task ExecuteJobExtractionAsync(ExtractionJob job, CancellationToken outerToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        if (!_runningExtractions.TryAdd(job.Id, cts))
        {
            cts.Dispose();
            return;
        }

        try
        {
            job.Status = nameof(ExtractionStatus.Extracting);
            job.StartedAt = DateTime.UtcNow;
            job.ProgressPercent = 0;
            job.ErrorMessage = null;
            _jobRepo.Update(job);
            _signalR.BroadcastMessage("queue", "updated", job);

            await _eventAggregator.PublishEventAsync(new ExtractionStartedEvent(job), cts.Token);

            var outcome = await _extractor.ExtractArchiveAsync(
                job.ArchivePath,
                job.LocalSourcePath,
                job.ManualPassword,
                progress =>
                {
                    job.ExtractedBytes = progress.ExtractedBytes;
                    job.TotalBytes = progress.TotalBytes;
                    job.ProgressPercent = progress.ProgressPercent;
                    job.SpeedBytesPerSec = progress.SpeedBytesPerSec;
                    job.EtaSeconds = progress.EtaSeconds;
                    _signalR.BroadcastMessage("queue", "updated", job);
                },
                cts.Token);

            if (outcome.Success)
            {
                job.ProgressPercent = 100.0;
                job.ExtractedBytes = outcome.TotalArchiveBytes;
                job.TotalBytes = outcome.TotalArchiveBytes;
                job.SpeedBytesPerSec = 0;
                job.EtaSeconds = 0;
                job.ExtractedAt = DateTime.UtcNow;
                job.SetExtractedFiles(outcome.ExtractedFilePaths);

                _historyRepo.Insert(new ExtractionHistory
                {
                    ExtractionJobId = job.Id,
                    ArrInstanceId = job.ArrInstanceId,
                    WatchFolderId = job.WatchFolderId,
                    InstanceName = job.InstanceName,
                    InstanceType = job.InstanceType,
                    DownloadId = job.DownloadId,
                    ReleaseTitle = job.ReleaseTitle,
                    ArchivePath = job.ArchivePath,
                    ArchiveType = job.ArchiveType,
                    EventType = "Extracted",
                    Status = "Extracted",
                    ArchiveSizeBytes = outcome.TotalArchiveBytes,
                    ExtractedFileCount = outcome.ExtractedFilePaths.Count,
                    ExtractedFilesJson = job.ExtractedFilesJson,
                    DurationMs = outcome.DurationMs,
                    Timestamp = DateTime.UtcNow
                });

                await _eventAggregator.PublishEventAsync(
                    new ExtractionCompletedEvent(job, outcome.ExtractedFilePaths.Count, outcome.DurationMs),
                    cts.Token);

                if (job.ArrInstanceId.HasValue)
                {
                    // State 4: Extracted (Pending Import) -> trigger RefreshMonitoredDownloads on *arr
                    job.Status = nameof(ExtractionStatus.ExtractedPendingImport);
                    _jobRepo.Update(job);
                    _signalR.BroadcastMessage("queue", "updated", job);

                    var instance = _instanceRepo.Find(job.ArrInstanceId.Value);
                    if (instance != null)
                    {
                        await _arrApiClient.TriggerRefreshMonitoredDownloadsAsync(instance, cts.Token);
                    }
                }
                else if (job.WatchFolderId.HasValue)
                {
                    var wf = _watchFolderRepo.Find(job.WatchFolderId.Value);
                    if (wf != null && wf.DeleteExtractedFilesDelaySeconds > 0)
                    {
                        TransitionToImportedPendingCleanup(job, wf.DeleteExtractedFilesDelaySeconds);
                    }
                    else
                    {
                        // WatchFolder without extracted-file deletion: optionally delete original archive if configured
                        if (job.DeleteOriginalArchives && File.Exists(job.ArchivePath))
                        {
                            TryDeleteOriginalArchiveSet(job.ArchivePath);
                        }

                        job.Status = nameof(ExtractionStatus.Completed);
                        job.CompletedAt = DateTime.UtcNow;
                        _jobRepo.Update(job);
                        _signalR.BroadcastMessage("queue", "deleted", new { id = job.Id });
                    }
                }
            }
            else
            {
                await HandleJobFailureAsync(job, outcome.ErrorMessage ?? "Unknown extraction error", outcome.ErrorStackTrace, outcome.DurationMs, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Info("Extraction job #{0} ({1}) was cancelled", job.Id, job.ReleaseTitle);
        }
        catch (Exception ex)
        {
            await HandleJobFailureAsync(job, ex.Message, ex.ToString(), 0, CancellationToken.None);
        }
        finally
        {
            _runningExtractions.TryRemove(job.Id, out _);
            cts.Dispose();
        }
    }

    private async Task HandleJobFailureAsync(ExtractionJob job, string errorMessage, string? stackTrace, long durationMs, CancellationToken token)
    {
        job.RetryCount++;
        job.ErrorMessage = errorMessage;
        job.ErrorStackTrace = stackTrace;
        job.SpeedBytesPerSec = 0;
        job.EtaSeconds = 0;

        if (job.RetryCount >= Math.Max(1, job.MaxRetries))
        {
            job.Status = nameof(ExtractionStatus.Failed);
            job.CompletedAt = DateTime.UtcNow;
            _jobRepo.Update(job);

            // Add to Blocklist so we don't infinitely loop on a corrupt/missing-password RAR
            var existingBlock = _blocklistRepo.FindByDownloadOrArchive(job.DownloadId, job.ArchivePath);
            if (existingBlock == null)
            {
                _blocklistRepo.Insert(new BlocklistItem
                {
                    ArrInstanceId = job.ArrInstanceId,
                    WatchFolderId = job.WatchFolderId,
                    InstanceName = job.InstanceName,
                    DownloadId = job.DownloadId,
                    ReleaseTitle = job.ReleaseTitle,
                    ArchivePath = job.ArchivePath,
                    ArchiveType = job.ArchiveType,
                    FailureCount = job.RetryCount,
                    ErrorMessage = errorMessage,
                    ErrorStackTrace = stackTrace,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existingBlock.FailureCount = job.RetryCount;
                existingBlock.ErrorMessage = errorMessage;
                existingBlock.ErrorStackTrace = stackTrace;
                _blocklistRepo.Update(existingBlock);
            }

            _historyRepo.Insert(new ExtractionHistory
            {
                ExtractionJobId = job.Id,
                ArrInstanceId = job.ArrInstanceId,
                WatchFolderId = job.WatchFolderId,
                InstanceName = job.InstanceName,
                InstanceType = job.InstanceType,
                DownloadId = job.DownloadId,
                ReleaseTitle = job.ReleaseTitle,
                ArchivePath = job.ArchivePath,
                ArchiveType = job.ArchiveType,
                EventType = "Failed",
                Status = nameof(ExtractionStatus.Failed),
                ArchiveSizeBytes = job.TotalBytes,
                ExtractedFileCount = 0,
                DurationMs = durationMs,
                ErrorMessage = errorMessage,
                ErrorStackTrace = stackTrace,
                Timestamp = DateTime.UtcNow
            });

            await _eventAggregator.PublishEventAsync(new ExtractionFailedEvent(job, errorMessage), token);
            _signalR.BroadcastMessage("blocklist", "updated", new { jobId = job.Id });
        }
        else
        {
            // Re-queue for retry
            job.Status = nameof(ExtractionStatus.Queued);
            _jobRepo.Update(job);
        }

        _signalR.BroadcastMessage("queue", "updated", job);
    }

    public async Task ProcessPendingCleanupsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var dueJobs = _jobRepo.GetActiveQueue()
            .Where(j => j.Status == nameof(ExtractionStatus.ImportedPendingCleanup)
                        && (!j.CleanupScheduledAt.HasValue || j.CleanupScheduledAt.Value <= now))
            .ToList();

        foreach (var job in dueJobs)
        {
            var extractedFiles = job.GetExtractedFiles();
            var deletedCount = 0;

            foreach (var filePath in extractedFiles)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        _diskProvider.DeleteFileSafe(filePath);
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Could not delete extracted leftover file {0}", filePath);
                }
            }

            // Optionally delete original archive ONLY if DeleteOriginalArchives is explicitly enabled
            if (job.DeleteOriginalArchives && File.Exists(job.ArchivePath))
            {
                TryDeleteOriginalArchiveSet(job.ArchivePath);
            }

            job.Status = nameof(ExtractionStatus.Completed);
            job.CompletedAt = DateTime.UtcNow;
            _jobRepo.Update(job);

            _historyRepo.Insert(new ExtractionHistory
            {
                ExtractionJobId = job.Id,
                ArrInstanceId = job.ArrInstanceId,
                WatchFolderId = job.WatchFolderId,
                InstanceName = job.InstanceName,
                InstanceType = job.InstanceType,
                DownloadId = job.DownloadId,
                ReleaseTitle = job.ReleaseTitle,
                ArchivePath = job.ArchivePath,
                ArchiveType = job.ArchiveType,
                EventType = "CleanedUp",
                Status = nameof(ExtractionStatus.Completed),
                ArchiveSizeBytes = job.TotalBytes,
                ExtractedFileCount = deletedCount,
                ExtractedFilesJson = job.ExtractedFilesJson,
                DurationMs = 0,
                Timestamp = DateTime.UtcNow
            });

            Logger.Info("Cleaned up {0} extracted leftover file(s) for '{1}' (original archive preserved for seeding: {2})",
                deletedCount, job.ReleaseTitle, !job.DeleteOriginalArchives);

            await _eventAggregator.PublishEventAsync(new ExtractionCleanedUpEvent(job, deletedCount), cancellationToken);
            _signalR.BroadcastMessage("queue", "deleted", new { id = job.Id });
            _signalR.BroadcastMessage("history", "created", new { jobId = job.Id });
        }
    }

    public async Task HandleWebhookAsync(ArrWebhookPayload payload, CancellationToken cancellationToken = default)
    {
        var eventType = payload.EventType?.Trim() ?? "";
        Logger.Info("Received *arr webhook event '{0}' for DownloadId '{1}'", eventType, payload.DownloadId ?? "N/A");

        if (eventType.Equals("Test", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // If ImportComplete / Download (which in Sonarr/Radarr webhook means Imported!), check if we have a pending import job
        if ((eventType.Equals("Download", StringComparison.OrdinalIgnoreCase) ||
             eventType.Equals("ImportComplete", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(payload.DownloadId))
        {
            var existingJob = _jobRepo.FindByDownloadId(payload.DownloadId);
            if (existingJob != null && existingJob.Status == nameof(ExtractionStatus.ExtractedPendingImport))
            {
                var config = _configService.GetExtractionConfig();
                TransitionToImportedPendingCleanup(existingJob, config.DeleteDelaySeconds);
                await ProcessPendingCleanupsAsync(cancellationToken);
                return;
            }
        }

        // Otherwise trigger immediate poll of all enabled instances
        await PollAllEnabledInstancesAsync(cancellationToken);
    }

    public bool CancelJob(int jobId)
    {
        if (_runningExtractions.TryRemove(jobId, out var cts))
        {
            cts.Cancel();
        }

        var job = _jobRepo.Find(jobId);
        if (job == null)
        {
            return false;
        }

        _jobRepo.Delete(jobId);
        _signalR.BroadcastMessage("queue", "deleted", new { id = jobId });
        return true;
    }

    public async Task<ExtractionJob?> ForceExtractJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        var job = _jobRepo.Find(jobId);
        if (job == null)
        {
            return null;
        }

        job.Status = nameof(ExtractionStatus.Queued);
        job.SyncthingWait = false;
        job.ErrorMessage = null;
        _jobRepo.Update(job);

        await ExecuteJobExtractionAsync(job, cancellationToken);
        return _jobRepo.Find(jobId);
    }

    public async Task<ExtractionJob?> RetryJobAsync(int jobId, string? manualPassword = null, CancellationToken cancellationToken = default)
    {
        var job = _jobRepo.Find(jobId);
        if (job == null)
        {
            return null;
        }

        var block = _blocklistRepo.FindByDownloadOrArchive(job.DownloadId, job.ArchivePath);
        if (block != null)
        {
            _blocklistRepo.Delete(block.Id);
        }

        job.RetryCount = 0;
        job.Status = nameof(ExtractionStatus.Queued);
        job.ErrorMessage = null;
        job.ErrorStackTrace = null;
        if (!string.IsNullOrWhiteSpace(manualPassword))
        {
            job.ManualPassword = manualPassword;
        }
        _jobRepo.Update(job);
        _signalR.BroadcastMessage("queue", "updated", job);

        await ProcessExtractionQueueAsync(cancellationToken);
        return _jobRepo.Find(jobId);
    }

    public async Task<ExtractionJob?> RetryBlocklistItemAsync(int blocklistId, string? manualPassword = null, CancellationToken cancellationToken = default)
    {
        var block = _blocklistRepo.Find(blocklistId);
        if (block == null)
        {
            return null;
        }

        var job = _jobRepo.FindByArchivePath(block.ArchivePath) ?? _jobRepo.FindByDownloadId(block.DownloadId);
        _blocklistRepo.Delete(blocklistId);
        _signalR.BroadcastMessage("blocklist", "deleted", new { id = blocklistId });

        if (job != null)
        {
            job.RetryCount = 0;
            job.Status = nameof(ExtractionStatus.Queued);
            job.ErrorMessage = null;
            job.ErrorStackTrace = null;
            if (!string.IsNullOrWhiteSpace(manualPassword))
            {
                job.ManualPassword = manualPassword;
            }
            _jobRepo.Update(job);
            _signalR.BroadcastMessage("queue", "updated", job);
            await ProcessExtractionQueueAsync(cancellationToken);
            return _jobRepo.Find(job.Id);
        }

        return null;
    }

    private void TryDeleteOriginalArchiveSet(string primaryArchivePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(primaryArchivePath);
            var fileName = Path.GetFileName(primaryArchivePath);
            var baseName = Path.GetFileNameWithoutExtension(primaryArchivePath);
            if (File.Exists(primaryArchivePath))
            {
                _diskProvider.DeleteFileSafe(primaryArchivePath);
            }

            if (dir != null && Directory.Exists(dir))
            {
                // Strip .part01 / .part1 suffix if multi-part RAR naming is used
                var stem = System.Text.RegularExpressions.Regex.Replace(
                    baseName,
                    @"\.part0*\d+$",
                    string.Empty,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                var multiPartPattern = new System.Text.RegularExpressions.Regex(
                    @"^" + System.Text.RegularExpressions.Regex.Escape(stem) + @"(\.part\d+\.rar|\.r\d{2,3}|\.s\d{2,3}|\.t\d{2,3}|\.z\d{2,3}|\.\d{3})$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                foreach (var sibling in Directory.GetFiles(dir))
                {
                    var siblingName = Path.GetFileName(sibling);
                    if (multiPartPattern.IsMatch(siblingName))
                    {
                        _diskProvider.DeleteFileSafe(sibling);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to delete original archive {0}", primaryArchivePath);
        }
    }
}

