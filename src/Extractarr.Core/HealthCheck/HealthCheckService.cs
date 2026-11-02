using Extractarr.Common.Disk;
using Extractarr.Core.Instances;
using Extractarr.Core.Messaging.Commands;
using Extractarr.Core.RemotePathMappings;
using Extractarr.Core.WatchFolders;
using Extractarr.SignalR;

namespace Extractarr.Core.HealthCheck;

public class HealthCheckResult
{
    public string Source { get; set; } = string.Empty;
    public string Type { get; set; } = "warning"; // notice, warning, error
    public string Message { get; set; } = string.Empty;
    public string WikiUrl { get; set; } = "https://wiki.servarr.com/unpackarr";
}

public class CheckHealthCommand : Command
{
    public override bool SendUpdatesToClient => true;
}

public interface IHealthCheckService
{
    List<HealthCheckResult> PerformHealthChecks();
}

public class HealthCheckService : IHealthCheckService, IExecute<CheckHealthCommand>
{
    private readonly IArrInstanceRepository _instanceRepo;
    private readonly IWatchFolderRepository _watchFolderRepo;
    private readonly IRemotePathMappingService _remotePathMappingService;
    private readonly IDiskProvider _diskProvider;
    private readonly IBroadcastSignalRMessage _signalR;

    public HealthCheckService(
        IArrInstanceRepository instanceRepo,
        IWatchFolderRepository watchFolderRepo,
        IRemotePathMappingService remotePathMappingService,
        IDiskProvider diskProvider,
        IBroadcastSignalRMessage signalR)
    {
        _instanceRepo = instanceRepo;
        _watchFolderRepo = watchFolderRepo;
        _remotePathMappingService = remotePathMappingService;
        _diskProvider = diskProvider;
        _signalR = signalR;
    }

    public void Execute(CheckHealthCommand message)
    {
        var results = PerformHealthChecks();
        _signalR.BroadcastMessage("health", "sync", results);
    }

    public List<HealthCheckResult> PerformHealthChecks()
    {
        var results = new List<HealthCheckResult>();
        var instances = _instanceRepo.All().ToList();
        var watchFolders = _watchFolderRepo.All().ToList();

        if (instances.Count == 0 && watchFolders.Count == 0)
        {
            results.Add(new HealthCheckResult
            {
                Source = "InstanceCheck",
                Type = "notice",
                Message = "No *arr instances or Watch Folders are configured. Add an instance in Instances or a Watch Folder to start automatic extraction.",
                WikiUrl = "https://wiki.servarr.com/unpackarr#instances"
            });
        }

        foreach (var inst in instances.Where(i => i.Enable))
        {
            if (inst.HealthStatus.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
                inst.HealthStatus.Equals("Offline", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new HealthCheckResult
                {
                    Source = $"ArrInstanceCheck:{inst.Name}",
                    Type = "error",
                    Message = $"{inst.InstanceType} instance '{inst.Name}' ({inst.BaseUrl}) is unreachable: {inst.LastErrorMessage ?? "Connection failed"}",
                    WikiUrl = "https://wiki.servarr.com/unpackarr#connection-errors"
                });
            }
        }

        foreach (var unmapped in _remotePathMappingService.GetUnmappedRemotePaths())
        {
            results.Add(new HealthCheckResult
            {
                Source = "RemotePathMappingCheck",
                Type = "warning",
                Message = $"{unmapped}. Configure a Remote Path Mapping in Settings -> Remote Path Mappings.",
                WikiUrl = "https://wiki.servarr.com/unpackarr#remote-path-mappings"
            });
        }

        foreach (var wf in watchFolders.Where(w => w.Enable))
        {
            if (!Directory.Exists(wf.Path))
            {
                results.Add(new HealthCheckResult
                {
                    Source = $"WatchFolderCheck:{wf.Name}",
                    Type = "warning",
                    Message = $"Watch Folder '{wf.Name}' path does not exist or is inaccessible: {wf.Path}",
                    WikiUrl = "https://wiki.servarr.com/unpackarr#watch-folders"
                });
            }
        }

        foreach (var disk in _diskProvider.GetDiskSpace())
        {
            // Warn if free space < 2 GB
            if (disk.FreeSpace > 0 && disk.FreeSpace < 2L * 1024 * 1024 * 1024)
            {
                var freeMb = disk.FreeSpace / (1024 * 1024);
                results.Add(new HealthCheckResult
                {
                    Source = "DiskSpaceCheck",
                    Type = "warning",
                    Message = $"Disk space on '{disk.Path}' is critically low ({freeMb} MB free). Archive extraction requires temporary disk space.",
                    WikiUrl = "https://wiki.servarr.com/unpackarr#disk-space"
                });
            }
        }

        return results;
    }
}

