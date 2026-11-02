using NLog;
using Extractarr.Common.Instrumentation;
using Extractarr.Core.Extraction;
using Extractarr.Core.HealthCheck;
using Extractarr.Core.Messaging.Commands;
using Extractarr.Core.WatchFolders;

namespace Extractarr.Core.Jobs;

public class PollArrInstancesCommand : Command
{
    public int? InstanceId { get; set; }
}

public class ProcessExtractionQueueCommand : Command
{
}

public class CleanupExtractedFilesCommand : Command
{
}

public class ScanWatchFoldersCommand : Command
{
    public int? WatchFolderId { get; set; }
}

public class HousekeepingCommand : Command
{
}

public class PollArrInstancesTask : IScheduledTask, IExecuteAsync<PollArrInstancesCommand>
{
    private readonly IExtractionLifecycleCoordinator _coordinator;
    public Type CommandType => typeof(PollArrInstancesCommand);
    public TimeSpan DefaultInterval => TimeSpan.FromSeconds(15);

    public PollArrInstancesTask(IExtractionLifecycleCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public async Task ExecuteAsync(PollArrInstancesCommand message, CancellationToken cancellationToken = default)
    {
        if (message.InstanceId.HasValue)
        {
            await _coordinator.PollSingleInstanceAsync(message.InstanceId.Value, cancellationToken);
        }
        else
        {
            await _coordinator.PollAllEnabledInstancesAsync(cancellationToken);
        }
    }
}

public class ProcessExtractionQueueTask : IScheduledTask, IExecuteAsync<ProcessExtractionQueueCommand>
{
    private readonly IExtractionLifecycleCoordinator _coordinator;
    public Type CommandType => typeof(ProcessExtractionQueueCommand);
    public TimeSpan DefaultInterval => TimeSpan.FromSeconds(10);

    public ProcessExtractionQueueTask(IExtractionLifecycleCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public async Task ExecuteAsync(ProcessExtractionQueueCommand message, CancellationToken cancellationToken = default)
    {
        await _coordinator.ProcessExtractionQueueAsync(cancellationToken);
    }
}

public class CleanupExtractedFilesTask : IScheduledTask, IExecuteAsync<CleanupExtractedFilesCommand>
{
    private readonly IExtractionLifecycleCoordinator _coordinator;
    public Type CommandType => typeof(CleanupExtractedFilesCommand);
    public TimeSpan DefaultInterval => TimeSpan.FromSeconds(15);

    public CleanupExtractedFilesTask(IExtractionLifecycleCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public async Task ExecuteAsync(CleanupExtractedFilesCommand message, CancellationToken cancellationToken = default)
    {
        await _coordinator.ProcessPendingCleanupsAsync(cancellationToken);
    }
}

public class ScanWatchFoldersTask : IScheduledTask, IExecuteAsync<ScanWatchFoldersCommand>
{
    private readonly IWatchFolderService _watchFolderService;
    public Type CommandType => typeof(ScanWatchFoldersCommand);
    public TimeSpan DefaultInterval => TimeSpan.FromMinutes(1);

    public ScanWatchFoldersTask(IWatchFolderService watchFolderService)
    {
        _watchFolderService = watchFolderService;
    }

    public async Task ExecuteAsync(ScanWatchFoldersCommand message, CancellationToken cancellationToken = default)
    {
        if (message.WatchFolderId.HasValue)
        {
            await _watchFolderService.ScanWatchFolderAsync(message.WatchFolderId.Value, cancellationToken);
        }
        else
        {
            await _watchFolderService.ScanAllWatchFoldersAsync(cancellationToken);
        }
    }
}

public class CheckHealthTask : IScheduledTask
{
    public Type CommandType => typeof(CheckHealthCommand);
    public TimeSpan DefaultInterval => TimeSpan.FromMinutes(5);
}

public class HousekeepingTask : IScheduledTask, IExecute<HousekeepingCommand>
{
    private readonly IExtractionHistoryRepository _historyRepo;
    private static readonly Logger Logger = ExtractarrLogger.GetLogger<HousekeepingTask>();

    public Type CommandType => typeof(HousekeepingCommand);
    public TimeSpan DefaultInterval => TimeSpan.FromHours(24);

    public HousekeepingTask(IExtractionHistoryRepository historyRepo)
    {
        _historyRepo = historyRepo;
    }

    public void Execute(HousekeepingCommand message)
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);
        var oldRecords = _historyRepo.All().Where(h => h.Timestamp < cutoff).Select(h => h.Id).ToList();
        if (oldRecords.Count > 0)
        {
            _historyRepo.DeleteMany(oldRecords);
            Logger.Info("Housekeeping pruned {0} history records older than 90 days", oldRecords.Count);
        }
    }
}

