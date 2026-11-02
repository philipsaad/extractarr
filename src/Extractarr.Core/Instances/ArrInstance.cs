using System.Text.Json;
using Dapper;
using Extractarr.Core.Datastore;

namespace Extractarr.Core.Instances;

public enum ArrInstanceType
{
    Sonarr,
    Radarr,
    Lidarr,
    Readarr,
    Whisparr
}

public class ArrInstance : ModelBase
{
    public string Name { get; set; } = string.Empty;
    public bool Enable { get; set; } = true;
    public string InstanceType { get; set; } = "Sonarr"; // Sonarr, Radarr, Lidarr, Readarr, Whisparr
    public string BaseUrl { get; set; } = "http://localhost:8989";
    public string ApiKey { get; set; } = string.Empty;
    public int PollIntervalSeconds { get; set; } = 15;
    public int TimeoutSeconds { get; set; } = 15;
    public bool VerifySsl { get; set; }
    public string MonitoredProtocols { get; set; } = "[\"torrent\",\"usenet\"]";
    public string MonitoredCategories { get; set; } = "[]";
    public bool DeleteOriginalArchives { get; set; }
    public bool SyncthingWait { get; set; } = true;
    public string HealthStatus { get; set; } = "Unknown"; // Online, Offline, Error, Unknown
    public string? LastErrorMessage { get; set; }
    public string? Version { get; set; }
    public int ActiveQueueCount { get; set; }
    public DateTime? LastPolledAt { get; set; }

    public List<string> GetProtocolsList()
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(MonitoredProtocols) ?? new List<string> { "torrent", "usenet" };
        }
        catch
        {
            return MonitoredProtocols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }

    public List<string> GetCategoriesList()
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(MonitoredCategories) ?? new List<string>();
        }
        catch
        {
            return MonitoredCategories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }
}

public interface IArrInstanceRepository : IBasicRepository<ArrInstance>
{
    List<ArrInstance> GetEnabled();
}

public class ArrInstanceRepository : BasicRepository<ArrInstance>, IArrInstanceRepository
{
    public ArrInstanceRepository(IDatabase database) : base(database, "ArrInstances")
    {
    }

    public List<ArrInstance> GetEnabled()
    {
        using var conn = _database.OpenConnection();
        return conn.Query<ArrInstance>("SELECT * FROM \"ArrInstances\" WHERE \"Enable\" = 1 ORDER BY \"Id\" ASC").ToList();
    }
}

