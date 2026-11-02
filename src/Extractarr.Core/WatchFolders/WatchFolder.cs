using Dapper;
using Extractarr.Core.Datastore;

namespace Extractarr.Core.WatchFolders;

public class WatchFolder : ModelBase
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? TargetPath { get; set; }
    public bool Enable { get; set; } = true;
    public bool Recursive { get; set; } = true;
    public bool DeleteOriginalArchives { get; set; }
    public int DeleteExtractedFilesDelaySeconds { get; set; }
    public bool SyncthingWait { get; set; } = true;
    public DateTime? LastScannedAt { get; set; }
}

public interface IWatchFolderRepository : IBasicRepository<WatchFolder>
{
    List<WatchFolder> GetEnabled();
}

public class WatchFolderRepository : BasicRepository<WatchFolder>, IWatchFolderRepository
{
    public WatchFolderRepository(IDatabase database) : base(database, "WatchFolders")
    {
    }

    public List<WatchFolder> GetEnabled()
    {
        using var conn = _database.OpenConnection();
        return conn.Query<WatchFolder>("SELECT * FROM \"WatchFolders\" WHERE \"Enable\" = 1 ORDER BY \"Id\" ASC").ToList();
    }
}

