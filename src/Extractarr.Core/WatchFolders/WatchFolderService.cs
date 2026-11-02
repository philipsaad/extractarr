using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using NLog;
using Extractarr.Common.Disk;
using Extractarr.Common.Instrumentation;
using Extractarr.Core.Extraction;
using Extractarr.SignalR;

namespace Extractarr.Core.WatchFolders;

public interface IWatchFolderService
{
    Task ScanAllWatchFoldersAsync(CancellationToken cancellationToken = default);
    Task ScanWatchFolderAsync(int watchFolderId, CancellationToken cancellationToken = default);
    void RefreshWatchers();
}

public class WatchFolderService : BackgroundService, IWatchFolderService
{
    private readonly IWatchFolderRepository _repository;
    private readonly ISharpCompressExtractor _extractor;
    private readonly IExtractionLifecycleCoordinator _coordinator;
    private readonly IBroadcastSignalRMessage _signalR;
    private readonly ConcurrentDictionary<int, FileSystemWatcher> _watchers = new();
    private static readonly Logger Logger = ExtractarrLogger.GetLogger<WatchFolderService>();

    public WatchFolderService(
        IWatchFolderRepository repository,
        ISharpCompressExtractor extractor,
        IExtractionLifecycleCoordinator coordinator,
        IBroadcastSignalRMessage signalR)
    {
        _repository = repository;
        _extractor = extractor;
        _coordinator = coordinator;
        _signalR = signalR;
    }

    public void RefreshWatchers()
    {
        foreach (var kv in _watchers)
        {
            if (_watchers.TryRemove(kv.Key, out var oldWatcher))
            {
                oldWatcher.EnableRaisingEvents = false;
                oldWatcher.Dispose();
            }
        }

        var enabledFolders = _repository.GetEnabled();
        foreach (var folder in enabledFolders)
        {
            var resolvedPath = OsPathTranslator.TranslateToCurrentOs(folder.Path);
            if (!Directory.Exists(resolvedPath))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(resolvedPath)
                {
                    IncludeSubdirectories = folder.Recursive,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                var capturedFolder = folder;
                watcher.Created += (_, e) => OnFileDetected(capturedFolder, e.FullPath);
                watcher.Renamed += (_, e) => OnFileDetected(capturedFolder, e.FullPath);

                _watchers[folder.Id] = watcher;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Unable to attach FileSystemWatcher to WatchFolder '{0}' ({1})", folder.Name, resolvedPath);
            }
        }
    }

    private void OnFileDetected(WatchFolder folder, string fullPath)
    {
        try
        {
            var resolvedFullPath = OsPathTranslator.TranslateToCurrentOs(fullPath);
            var archives = _extractor.DiscoverPrimaryArchives(resolvedFullPath, folder.Recursive);
            foreach (var archive in archives)
            {
                _coordinator.EnqueueFromWatchFolder(folder, archive);
            }

            if (archives.Count > 0)
            {
                _ = _coordinator.ProcessExtractionQueueAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Error handling FileSystemWatcher event for {0}", fullPath);
        }
    }

    public async Task ScanAllWatchFoldersAsync(CancellationToken cancellationToken = default)
    {
        var enabled = _repository.GetEnabled();
        foreach (var folder in enabled)
        {
            await ScanFolderInternalAsync(folder, cancellationToken);
        }
    }

    public async Task ScanWatchFolderAsync(int watchFolderId, CancellationToken cancellationToken = default)
    {
        var folder = _repository.Find(watchFolderId);
        if (folder != null)
        {
            await ScanFolderInternalAsync(folder, cancellationToken);
        }
    }

    private async Task ScanFolderInternalAsync(WatchFolder folder, CancellationToken cancellationToken)
    {
        var resolvedPath = OsPathTranslator.TranslateToCurrentOs(folder.Path);
        if (!Directory.Exists(resolvedPath))
        {
            return;
        }

        var archives = _extractor.DiscoverPrimaryArchives(resolvedPath, folder.Recursive);
        foreach (var archive in archives)
        {
            _coordinator.EnqueueFromWatchFolder(folder, archive);
        }

        folder.LastScannedAt = DateTime.UtcNow;
        _repository.Update(folder);
        _signalR.BroadcastMessage("watchfolder", "updated", folder);

        await _coordinator.ProcessExtractionQueueAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        RefreshWatchers();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAllWatchFoldersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error in WatchFolder background polling loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}

