using FluentMigrator;

namespace Extractarr.Core.Datastore.Migration;

[Migration(1, "Initial Unpackarr SQLite Schema")]
public class InitialSetup : FluentMigrator.Migration
{
    public override void Up()
    {
        Create.Table("Config")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Key").AsString().NotNullable().Unique()
            .WithColumn("Value").AsString().NotNullable();

        Create.Table("ScheduledTasks")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TypeName").AsString().NotNullable().Unique()
            .WithColumn("Interval").AsInt32().NotNullable()
            .WithColumn("LastExecution").AsDateTime().NotNullable()
            .WithColumn("LastStartTime").AsDateTime().NotNullable();

        Create.Table("ArrInstances")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Enable").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("InstanceType").AsString().NotNullable()
            .WithColumn("BaseUrl").AsString().NotNullable()
            .WithColumn("ApiKey").AsString().NotNullable()
            .WithColumn("PollIntervalSeconds").AsInt32().NotNullable().WithDefaultValue(15)
            .WithColumn("TimeoutSeconds").AsInt32().NotNullable().WithDefaultValue(15)
            .WithColumn("VerifySsl").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("MonitoredProtocols").AsString().NotNullable().WithDefaultValue("[\"torrent\",\"usenet\"]")
            .WithColumn("MonitoredCategories").AsString().NotNullable().WithDefaultValue("[]")
            .WithColumn("DeleteOriginalArchives").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("SyncthingWait").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("HealthStatus").AsString().NotNullable().WithDefaultValue("Unknown")
            .WithColumn("LastErrorMessage").AsString().Nullable()
            .WithColumn("Version").AsString().Nullable()
            .WithColumn("ActiveQueueCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("LastPolledAt").AsDateTime().Nullable();

        Create.Table("RemotePathMappings")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Host").AsString().NotNullable()
            .WithColumn("RemotePath").AsString().NotNullable()
            .WithColumn("LocalPath").AsString().NotNullable();

        Create.Table("WatchFolders")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Path").AsString().NotNullable()
            .WithColumn("TargetPath").AsString().Nullable()
            .WithColumn("Enable").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("Recursive").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("DeleteOriginalArchives").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("DeleteExtractedFilesDelaySeconds").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("SyncthingWait").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("LastScannedAt").AsDateTime().Nullable();

        Create.Table("ExtractionJobs")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("ArrInstanceId").AsInt32().Nullable()
            .WithColumn("WatchFolderId").AsInt32().Nullable()
            .WithColumn("InstanceName").AsString().NotNullable()
            .WithColumn("InstanceType").AsString().NotNullable()
            .WithColumn("ExternalQueueId").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("DownloadId").AsString().NotNullable()
            .WithColumn("ReleaseTitle").AsString().NotNullable()
            .WithColumn("Protocol").AsString().NotNullable().WithDefaultValue("torrent")
            .WithColumn("Category").AsString().Nullable()
            .WithColumn("RemoteSourcePath").AsString().NotNullable()
            .WithColumn("LocalSourcePath").AsString().NotNullable()
            .WithColumn("ArchivePath").AsString().NotNullable()
            .WithColumn("ArchiveType").AsString().NotNullable()
            .WithColumn("Status").AsString().NotNullable()
            .WithColumn("ProgressPercent").AsDouble().NotNullable().WithDefaultValue(0.0)
            .WithColumn("SpeedBytesPerSec").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("EtaSeconds").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("TotalBytes").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("ExtractedBytes").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("ExtractedFilesJson").AsString().NotNullable().WithDefaultValue("[]")
            .WithColumn("RetryCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("MaxRetries").AsInt32().NotNullable().WithDefaultValue(3)
            .WithColumn("ManualPassword").AsString().Nullable()
            .WithColumn("ErrorMessage").AsString().Nullable()
            .WithColumn("ErrorStackTrace").AsString().Nullable()
            .WithColumn("DeleteOriginalArchives").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("SyncthingWait").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("CreatedAt").AsDateTime().NotNullable()
            .WithColumn("StartedAt").AsDateTime().Nullable()
            .WithColumn("ExtractedAt").AsDateTime().Nullable()
            .WithColumn("ImportedAt").AsDateTime().Nullable()
            .WithColumn("CleanupScheduledAt").AsDateTime().Nullable()
            .WithColumn("CompletedAt").AsDateTime().Nullable();

        Create.Index("IX_ExtractionJobs_DownloadId").OnTable("ExtractionJobs").OnColumn("DownloadId");
        Create.Index("IX_ExtractionJobs_Status").OnTable("ExtractionJobs").OnColumn("Status");

        Create.Table("ExtractionHistory")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("ExtractionJobId").AsInt32().NotNullable()
            .WithColumn("ArrInstanceId").AsInt32().Nullable()
            .WithColumn("WatchFolderId").AsInt32().Nullable()
            .WithColumn("InstanceName").AsString().NotNullable()
            .WithColumn("InstanceType").AsString().NotNullable()
            .WithColumn("DownloadId").AsString().NotNullable()
            .WithColumn("ReleaseTitle").AsString().NotNullable()
            .WithColumn("ArchivePath").AsString().NotNullable()
            .WithColumn("ArchiveType").AsString().NotNullable()
            .WithColumn("EventType").AsString().NotNullable()
            .WithColumn("Status").AsString().NotNullable()
            .WithColumn("ArchiveSizeBytes").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("ExtractedFileCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("ExtractedFilesJson").AsString().NotNullable().WithDefaultValue("[]")
            .WithColumn("DurationMs").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("ErrorMessage").AsString().Nullable()
            .WithColumn("ErrorStackTrace").AsString().Nullable()
            .WithColumn("Timestamp").AsDateTime().NotNullable();

        Create.Index("IX_ExtractionHistory_Timestamp").OnTable("ExtractionHistory").OnColumn("Timestamp");

        Create.Table("Blocklist")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("ArrInstanceId").AsInt32().Nullable()
            .WithColumn("WatchFolderId").AsInt32().Nullable()
            .WithColumn("InstanceName").AsString().NotNullable()
            .WithColumn("DownloadId").AsString().NotNullable()
            .WithColumn("ReleaseTitle").AsString().NotNullable()
            .WithColumn("ArchivePath").AsString().NotNullable()
            .WithColumn("ArchiveType").AsString().NotNullable()
            .WithColumn("FailureCount").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("ErrorMessage").AsString().Nullable()
            .WithColumn("ErrorStackTrace").AsString().Nullable()
            .WithColumn("ManualPassword").AsString().Nullable()
            .WithColumn("CreatedAt").AsDateTime().NotNullable();

        Create.Table("Notifications")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Implementation").AsString().NotNullable()
            .WithColumn("Enable").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("OnExtractionStart").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("OnExtractionComplete").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("OnExtractionFailure").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("OnCleanup").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("SettingsJson").AsString().NotNullable().WithDefaultValue("{}");
    }

    public override void Down()
    {
        Delete.Table("Notifications");
        Delete.Table("Blocklist");
        Delete.Table("ExtractionHistory");
        Delete.Table("ExtractionJobs");
        Delete.Table("WatchFolders");
        Delete.Table("RemotePathMappings");
        Delete.Table("ArrInstances");
        Delete.Table("ScheduledTasks");
        Delete.Table("Config");
    }
}

