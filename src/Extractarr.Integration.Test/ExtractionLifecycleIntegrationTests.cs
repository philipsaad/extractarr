using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using Extractarr.Common.EnvironmentInfo;
using Extractarr.Core.Configuration;
using Extractarr.Core.Extraction;
using Extractarr.Core.Instances;
using Extractarr.Core.RemotePathMappings;
using Extractarr.Core.WatchFolders;
using Extractarr.Host;
using Xunit;

namespace Extractarr.Integration.Test;

public class FakeArrApiClient : IArrApiClient
{
    public List<ArrQueueRecordDto> CurrentQueue { get; set; } = new();
    public int RefreshCommandCount { get; private set; }

    public Task<ArrConnectionTestResult> TestConnectionAsync(ArrInstance instance, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ArrConnectionTestResult
        {
            IsValid = true,
            AppName = instance.InstanceType,
            Version = "1.0.0.0",
            Message = "Connected to mock instance"
        });
    }

    public Task<List<ArrQueueRecordDto>> GetQueueAsync(ArrInstance instance, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CurrentQueue.ToList());
    }

    public Task<bool> TriggerRefreshMonitoredDownloadsAsync(ArrInstance instance, CancellationToken cancellationToken = default)
    {
        RefreshCommandCount++;
        return Task.FromResult(true);
    }
}

public class ExtractionLifecycleIntegrationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly ServiceProvider _serviceProvider;
    private readonly FakeArrApiClient _fakeArrClient;

    public ExtractionLifecycleIntegrationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"UnpackarrTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        var services = new ServiceCollection();
        var appFolder = new AppFolderInfo(Path.Combine(_testRoot, "appdata"));
        UnpackarrBootstrap.RegisterUnpackarrServices(services, appFolder, registerHostedWorkers: false);

        _fakeArrClient = new FakeArrApiClient();
        services.AddSingleton<IArrApiClient>(_fakeArrClient);

        _serviceProvider = services.BuildServiceProvider();
        UnpackarrBootstrap.InitializeDatabase(_serviceProvider);
    }

    [Fact]
    public async Task FullArrExtractionToCleanupLifecycle_PreservesOriginalArchiveForSeeding()
    {
        var downloadDir = Path.Combine(_testRoot, "downloads", "Show.S01E01.1080p");
        Directory.CreateDirectory(downloadDir);

        // Create a real archive containing a simulated video file
        var archivePath = Path.Combine(downloadDir, "release.zip");
        using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("Show.S01E01.1080p.mkv");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("FAKE_MATROSKA_VIDEO_STREAM_PAYLOAD_DATA_1234567890");
        }

        // Configure zero delete delay for instant cleanup verification
        var configService = _serviceProvider.GetRequiredService<IConfigService>();
        var extConfig = configService.GetExtractionConfig();
        extConfig.DeleteDelaySeconds = 0;
        configService.SaveExtractionConfig(extConfig);

        // Configure Remote Path Mapping (/remote/torrents -> _testRoot/downloads)
        var mappingRepo = _serviceProvider.GetRequiredService<IRemotePathMappingRepository>();
        mappingRepo.Insert(new RemotePathMapping
        {
            Host = "Sonarr Main",
            RemotePath = "/remote/torrents",
            LocalPath = Path.Combine(_testRoot, "downloads")
        });

        // Configure Sonarr Instance
        var instanceRepo = _serviceProvider.GetRequiredService<IArrInstanceRepository>();
        var sonarr = instanceRepo.Insert(new ArrInstance
        {
            Name = "Sonarr Main",
            InstanceType = "Sonarr",
            Enable = true,
            BaseUrl = "http://sonarr:8989",
            ApiKey = "test-api-key",
            PollIntervalSeconds = 5,
            DeleteOriginalArchives = false, // Must preserve original archive for torrent seeding!
            SyncthingWait = true
        });

        // Simulate completed item in Sonarr queue with remote path
        _fakeArrClient.CurrentQueue = new List<ArrQueueRecordDto>
        {
            new()
            {
                Id = 101,
                DownloadId = "HASH_ABCD1234",
                Title = "Show.S01E01.1080p",
                Status = "completed",
                TrackedDownloadState = "importPending",
                Protocol = "torrent",
                OutputPath = "/remote/torrents/Show.S01E01.1080p",
                Size = 1000,
                SizeLeft = 0
            }
        };

        var coordinator = _serviceProvider.GetRequiredService<IExtractionLifecycleCoordinator>();
        var jobRepo = _serviceProvider.GetRequiredService<IExtractionJobRepository>();
        var historyRepo = _serviceProvider.GetRequiredService<IExtractionHistoryRepository>();

        // Step 1: Poll Sonarr -> Queues -> Extracts -> Transitions to ExtractedPendingImport & sends RefreshMonitoredDownloads
        await coordinator.PollSingleInstanceAsync(sonarr.Id);

        var extractedVideoFile = Path.Combine(downloadDir, "Show.S01E01.1080p.mkv");
        Assert.True(File.Exists(extractedVideoFile), "Video file should be extracted into the download directory");
        Assert.True(File.Exists(archivePath), "Original archive must still exist");
        Assert.Equal(1, _fakeArrClient.RefreshCommandCount);

        var jobAfterExtract = jobRepo.FindByDownloadId("HASH_ABCD1234");
        Assert.NotNull(jobAfterExtract);
        Assert.Equal(nameof(ExtractionStatus.ExtractedPendingImport), jobAfterExtract!.Status);

        // Step 2: Simulate Sonarr finishing import (item disappears from Sonarr queue)
        _fakeArrClient.CurrentQueue.Clear();
        await coordinator.PollSingleInstanceAsync(sonarr.Id);

        var jobAfterCleanup = jobRepo.FindByDownloadId("HASH_ABCD1234");
        Assert.NotNull(jobAfterCleanup);
        Assert.Equal(nameof(ExtractionStatus.Completed), jobAfterCleanup!.Status);

        // Verify extracted leftover file is deleted while original archive remains intact for seeding
        Assert.False(File.Exists(extractedVideoFile), "Extracted leftover file must be cleaned up after import");
        Assert.True(File.Exists(archivePath), "Original archive must remain intact so torrent seeding continues");

        // Verify History logged Extracted, Imported, and CleanedUp events
        var historyEvents = historyRepo.All().Select(h => h.EventType).ToList();
        Assert.Contains("Extracted", historyEvents);
        Assert.Contains("Imported", historyEvents);
        Assert.Contains("CleanedUp", historyEvents);
    }

    [Fact]
    public async Task SyncthingWaitAndCorruptArchiveBlocklist_StateTransitionsCorrectly()
    {
        var downloadDir = Path.Combine(_testRoot, "syncthing_test");
        Directory.CreateDirectory(downloadDir);

        // Create a valid archive + a Syncthing .tmp file in the same folder
        var archivePath = Path.Combine(downloadDir, "movie.zip");
        using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("Movie.2026.1080p.mkv");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("MOVIE_STREAM_BYTES");
        }

        var syncthingTmpFile = Path.Combine(downloadDir, ".syncthing.movie.zip.tmp");
        await File.WriteAllTextAsync(syncthingTmpFile, "syncing...");

        var wfRepo = _serviceProvider.GetRequiredService<IWatchFolderRepository>();
        var wf = wfRepo.Insert(new WatchFolder
        {
            Name = "Syncthing Folder",
            Path = downloadDir,
            Enable = true,
            Recursive = true,
            SyncthingWait = true
        });

        var coordinator = _serviceProvider.GetRequiredService<IExtractionLifecycleCoordinator>();
        var jobRepo = _serviceProvider.GetRequiredService<IExtractionJobRepository>();

        var queuedJob = coordinator.EnqueueFromWatchFolder(wf, archivePath);
        Assert.NotNull(queuedJob);

        // Attempt extraction while .tmp file is present -> must transition to Waiting
        await coordinator.ProcessExtractionQueueAsync();
        var waitingJob = jobRepo.Get(queuedJob!.Id);
        Assert.Equal(nameof(ExtractionStatus.Waiting), waitingJob.Status);

        // Remove Syncthing .tmp file and process again -> must extract and complete
        File.Delete(syncthingTmpFile);
        await coordinator.ProcessExtractionQueueAsync();

        var completedJob = jobRepo.Get(queuedJob.Id);
        Assert.Equal(nameof(ExtractionStatus.Completed), completedJob.Status);
        Assert.True(File.Exists(Path.Combine(downloadDir, "Movie.2026.1080p.mkv")));

        // Corrupt archive should transition to Failed and enter Blocklist when MaxRetries = 1
        var configService = _serviceProvider.GetRequiredService<IConfigService>();
        var cfg = configService.GetExtractionConfig();
        cfg.MaxRetries = 1;
        configService.SaveExtractionConfig(cfg);

        var corruptPath = Path.Combine(downloadDir, "corrupt_release.rar");
        await File.WriteAllTextAsync(corruptPath, "NOT_A_VALID_RAR_HEADER_AT_ALL");

        var corruptJob = coordinator.EnqueueFromWatchFolder(wf, corruptPath);
        Assert.NotNull(corruptJob);
        await coordinator.ProcessExtractionQueueAsync();

        var failedJob = jobRepo.Get(corruptJob!.Id);
        Assert.Equal(nameof(ExtractionStatus.Failed), failedJob.Status);

        var blocklistRepo = _serviceProvider.GetRequiredService<IBlocklistRepository>();
        var blocked = blocklistRepo.FindByDownloadOrArchive(failedJob.DownloadId, corruptPath);
        Assert.NotNull(blocked);
    }

    [Fact]
    public async Task NestedArchiveAndWatchFolder_ExtractsRecursively()
    {
        var watchDir = Path.Combine(_testRoot, "watch");
        Directory.CreateDirectory(watchDir);

        // Create inner.zip containing final_payload.txt
        var innerZipPath = Path.Combine(_testRoot, "inner.zip");
        using (var innerZip = ZipFile.Open(innerZipPath, ZipArchiveMode.Create))
        {
            var entry = innerZip.CreateEntry("nested_episode.mkv");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("NESTED_ARCHIVE_CONTENT");
        }

        // Create outer.zip inside watchDir containing inner.zip
        var outerZipPath = Path.Combine(watchDir, "outer_release.zip");
        using (var outerZip = ZipFile.Open(outerZipPath, ZipArchiveMode.Create))
        {
            outerZip.CreateEntryFromFile(innerZipPath, "inner.zip");
        }

        var watchRepo = _serviceProvider.GetRequiredService<IWatchFolderRepository>();
        var wf = watchRepo.Insert(new WatchFolder
        {
            Name = "Downloads Watch",
            Path = watchDir,
            Enable = true,
            Recursive = true,
            DeleteOriginalArchives = false,
            DeleteExtractedFilesDelaySeconds = 0
        });

        var watchService = _serviceProvider.GetRequiredService<IWatchFolderService>();
        await watchService.ScanWatchFolderAsync(wf.Id);

        var finalUnpackedFile = Path.Combine(watchDir, "nested_episode.mkv");
        Assert.True(File.Exists(finalUnpackedFile), "Nested archive inside outer archive should be recursively extracted");
        Assert.Equal("NESTED_ARCHIVE_CONTENT", await File.ReadAllTextAsync(finalUnpackedFile));
    }

    [Fact]
    public void CrossPlatformPathTranslation_AutomaticallyTranslatesWindowsAndLinuxPaths()
    {
        // 1. Linux/WSL/Docker -> Windows translation
        Assert.Equal(
            @"C:\Users\philipsaad\Downloads\Show.S01E01",
            Extractarr.Common.Disk.OsPathTranslator.TranslatePath("/mnt/c/Users/philipsaad/Downloads/Show.S01E01", targetWindows: true));

        Assert.Equal(
            @"D:\Torrents\Movies\Film.2026",
            Extractarr.Common.Disk.OsPathTranslator.TranslatePath("/host_mnt/d/Torrents/Movies/Film.2026", targetWindows: true));

        Assert.Equal(
            @"\\nas\media\torrents\release",
            Extractarr.Common.Disk.OsPathTranslator.TranslatePath("//nas/media/torrents/release", targetWindows: true));

        // 2. Windows -> Linux/WSL translation
        Assert.Equal(
            "/mnt/c/Users/philipsaad/Downloads/Show.S01E01",
            Extractarr.Common.Disk.OsPathTranslator.TranslatePath(@"C:\Users\philipsaad\Downloads\Show.S01E01", targetWindows: false));

        Assert.Equal(
            "/mnt/d/Torrents/Movies/Film.2026",
            Extractarr.Common.Disk.OsPathTranslator.TranslatePath("D:/Torrents/Movies/Film.2026", targetWindows: false));

        Assert.Equal(
            "/mnt/nas/media/torrents/release",
            Extractarr.Common.Disk.OsPathTranslator.TranslatePath(@"\\nas\media\torrents\release", targetWindows: false));

        // 3. Automatic zero-config translation in RemotePathMappingService when a WSL/Docker path points to a real local directory
        var realLocalDir = Path.Combine(_testRoot, "auto_translated_release");
        Directory.CreateDirectory(realLocalDir);

        var linuxStyleForRealDir = Extractarr.Common.Disk.OsPathTranslator.TranslatePath(realLocalDir, targetWindows: false);
        var mappingService = _serviceProvider.GetRequiredService<IRemotePathMappingService>();
        var resolvedLocal = mappingService.MapRemoteToLocal("Sonarr Docker", "http://localhost:8989", linuxStyleForRealDir);

        Assert.Equal(realLocalDir, resolvedLocal, ignoreCase: true);
        Assert.True(Directory.Exists(resolvedLocal));

        // 4. Read-only file safe deletion (Windows NTFS / SMB torrent compatibility)
        var diskProvider = _serviceProvider.GetRequiredService<Extractarr.Common.Disk.IDiskProvider>();
        var readOnlyFile = Path.Combine(_testRoot, "readonly_archive_part.r00");
        File.WriteAllText(readOnlyFile, "READ_ONLY_TEST_DATA");
        File.SetAttributes(readOnlyFile, File.GetAttributes(readOnlyFile) | FileAttributes.ReadOnly);
        diskProvider.DeleteFileSafe(readOnlyFile);
        Assert.False(File.Exists(readOnlyFile));
    }

    public void Dispose()
    {
        try
        {
            _serviceProvider.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch
        {
            // Ignore temp cleanup errors
        }
    }
}

