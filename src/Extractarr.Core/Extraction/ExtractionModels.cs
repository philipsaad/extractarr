using System.Text.Json;
using Dapper;
using Extractarr.Core.Datastore;

namespace Extractarr.Core.Extraction;

public enum ExtractionStatus
{
    Queued,
    Waiting,
    Extracting,
    ExtractedPendingImport,
    ImportedPendingCleanup,
    Completed,
    Failed
}

public class ExtractionJob : ModelBase
{
    public int? ArrInstanceId { get; set; }
    public int? WatchFolderId { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public string InstanceType { get; set; } = "Sonarr";
    public long ExternalQueueId { get; set; }
    public string DownloadId { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    public string Protocol { get; set; } = "torrent";
    public string? Category { get; set; }
    public string RemoteSourcePath { get; set; } = string.Empty;
    public string LocalSourcePath { get; set; } = string.Empty;
    public string ArchivePath { get; set; } = string.Empty;
    public string ArchiveType { get; set; } = "rar";
    public string Status { get; set; } = nameof(ExtractionStatus.Queued);
    public double ProgressPercent { get; set; }
    public long SpeedBytesPerSec { get; set; }
    public int EtaSeconds { get; set; }
    public long TotalBytes { get; set; }
    public long ExtractedBytes { get; set; }
    public string ExtractedFilesJson { get; set; } = "[]";
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public string? ManualPassword { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }
    public bool DeleteOriginalArchives { get; set; }
    public bool SyncthingWait { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? ExtractedAt { get; set; }
    public DateTime? ImportedAt { get; set; }
    public DateTime? CleanupScheduledAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public List<string> GetExtractedFiles()
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(ExtractedFilesJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public void SetExtractedFiles(IEnumerable<string> files)
    {
        ExtractedFilesJson = JsonSerializer.Serialize(files.ToList());
    }
}

public class ExtractionHistory : ModelBase
{
    public int ExtractionJobId { get; set; }
    public int? ArrInstanceId { get; set; }
    public int? WatchFolderId { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public string InstanceType { get; set; } = "Sonarr";
    public string DownloadId { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    public string ArchivePath { get; set; } = string.Empty;
    public string ArchiveType { get; set; } = "rar";
    public string EventType { get; set; } = "Extracted"; // Extracted, Imported, CleanedUp, Failed
    public string Status { get; set; } = "Completed";
    public long ArchiveSizeBytes { get; set; }
    public int ExtractedFileCount { get; set; }
    public string ExtractedFilesJson { get; set; } = "[]";
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public List<string> GetExtractedFiles()
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(ExtractedFilesJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}

public class BlocklistItem : ModelBase
{
    public int? ArrInstanceId { get; set; }
    public int? WatchFolderId { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public string DownloadId { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    public string ArchivePath { get; set; } = string.Empty;
    public string ArchiveType { get; set; } = "rar";
    public int FailureCount { get; set; } = 1;
    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }
    public string? ManualPassword { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public interface IExtractionJobRepository : IBasicRepository<ExtractionJob>
{
    List<ExtractionJob> GetActiveQueue();
    ExtractionJob? FindByDownloadId(string downloadId);
    ExtractionJob? FindByArchivePath(string archivePath);
}

public class ExtractionJobRepository : BasicRepository<ExtractionJob>, IExtractionJobRepository
{
    public ExtractionJobRepository(IDatabase database) : base(database, "ExtractionJobs")
    {
    }

    public List<ExtractionJob> GetActiveQueue()
    {
        using var conn = _database.OpenConnection();
        return conn.Query<ExtractionJob>(
            "SELECT * FROM \"ExtractionJobs\" WHERE \"Status\" NOT IN ('Completed') ORDER BY \"CreatedAt\" DESC").ToList();
    }

    public ExtractionJob? FindByDownloadId(string downloadId)
    {
        using var conn = _database.OpenConnection();
        return conn.QueryFirstOrDefault<ExtractionJob>(
            "SELECT * FROM \"ExtractionJobs\" WHERE \"DownloadId\" = @DownloadId ORDER BY \"Id\" DESC LIMIT 1",
            new { DownloadId = downloadId });
    }

    public ExtractionJob? FindByArchivePath(string archivePath)
    {
        using var conn = _database.OpenConnection();
        return conn.QueryFirstOrDefault<ExtractionJob>(
            "SELECT * FROM \"ExtractionJobs\" WHERE \"ArchivePath\" = @ArchivePath ORDER BY \"Id\" DESC LIMIT 1",
            new { ArchivePath = archivePath });
    }
}

public interface IExtractionHistoryRepository : IBasicRepository<ExtractionHistory>
{
    List<ExtractionHistory> GetSince(DateTime date);
}

public class ExtractionHistoryRepository : BasicRepository<ExtractionHistory>, IExtractionHistoryRepository
{
    public ExtractionHistoryRepository(IDatabase database) : base(database, "ExtractionHistory")
    {
    }

    public List<ExtractionHistory> GetSince(DateTime date)
    {
        using var conn = _database.OpenConnection();
        return conn.Query<ExtractionHistory>(
            "SELECT * FROM \"ExtractionHistory\" WHERE \"Timestamp\" >= @Date ORDER BY \"Timestamp\" DESC",
            new { Date = date }).ToList();
    }
}

public interface IBlocklistRepository : IBasicRepository<BlocklistItem>
{
    BlocklistItem? FindByDownloadOrArchive(string downloadId, string archivePath);
}

public class BlocklistRepository : BasicRepository<BlocklistItem>, IBlocklistRepository
{
    public BlocklistRepository(IDatabase database) : base(database, "Blocklist")
    {
    }

    public BlocklistItem? FindByDownloadOrArchive(string downloadId, string archivePath)
    {
        using var conn = _database.OpenConnection();
        return conn.QueryFirstOrDefault<BlocklistItem>(
            "SELECT * FROM \"Blocklist\" WHERE (\"DownloadId\" = @DownloadId AND @DownloadId <> '') OR \"ArchivePath\" = @ArchivePath LIMIT 1",
            new { DownloadId = downloadId, ArchivePath = archivePath });
    }
}

