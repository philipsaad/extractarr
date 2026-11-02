using System.Security.Cryptography;
using Dapper;
using Extractarr.Common.EnvironmentInfo;
using Extractarr.Common.Instrumentation;
using Extractarr.Core.Datastore;

namespace Extractarr.Core.Configuration;

public class Config : ModelBase
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public interface IConfigRepository : IBasicRepository<Config>
{
    Config? GetByKey(string key);
    void SetValue(string key, string value);
    Dictionary<string, string> GetAllDictionary();
}

public class ConfigRepository : BasicRepository<Config>, IConfigRepository
{
    public ConfigRepository(IDatabase database) : base(database, "Config")
    {
    }

    public Config? GetByKey(string key)
    {
        using var conn = _database.OpenConnection();
        return conn.QuerySingleOrDefault<Config>(
            "SELECT * FROM \"Config\" WHERE LOWER(\"Key\") = LOWER(@Key)",
            new { Key = key });
    }

    public void SetValue(string key, string value)
    {
        var existing = GetByKey(key);
        if (existing == null)
        {
            Insert(new Config { Key = key, Value = value });
        }
        else
        {
            existing.Value = value;
            Update(existing);
        }
    }

    public Dictionary<string, string> GetAllDictionary()
    {
        return All().ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
    }
}

public class ExtractionConfigDto
{
    public int Id { get; set; } = 1;
    public int MaxConcurrentExtractions { get; set; } = 2;
    public int BufferSizeKb { get; set; } = 256;
    public string TempFolderName { get; set; } = "_unpackerred";
    public string FilePermissions { get; set; } = "0644";
    public string FolderPermissions { get; set; } = "0755";
    public string ChownUid { get; set; } = string.Empty;
    public string ChownGid { get; set; } = string.Empty;
    public List<string> ArchivePasswords { get; set; } = new();
    public int DeleteDelaySeconds { get; set; } = 300;
    public int MaxRetries { get; set; } = 3;
}

public class HostConfigDto
{
    public int Id { get; set; } = 1;
    public string BindAddress { get; set; } = "*";
    public int Port { get; set; } = 8686;
    public string UrlBase { get; set; } = string.Empty;
    public bool EnableSsl { get; set; }
    public int SslPort { get; set; } = 9898;
    public string AuthenticationMethod { get; set; } = "none";
    public string AuthenticationRequired { get; set; } = "enabled";
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string LogLevel { get; set; } = "info";
    public bool AnalyticsEnabled { get; set; }
    public bool ProxyEnabled { get; set; }
    public string ProxyHost { get; set; } = string.Empty;
    public int ProxyPort { get; set; } = 8080;
    public string InstanceName { get; set; } = "Extractarr";
}

public class UiConfigDto
{
    public int Id { get; set; } = 1;
    public string Theme { get; set; } = "auto";
    public string ShortDateFormat { get; set; } = "MMM D YYYY";
    public string LongDateFormat { get; set; } = "dddd, MMMM D YYYY";
    public string TimeFormat { get; set; } = "HH:mm";
    public bool ShowRelativeDates { get; set; } = true;
    public bool EnableColorImpairedMode { get; set; }
    public int FirstDayOfWeek { get; set; }
}

public interface IConfigService
{
    ExtractionConfigDto GetExtractionConfig();
    ExtractionConfigDto SaveExtractionConfig(ExtractionConfigDto dto);
    HostConfigDto GetHostConfig();
    HostConfigDto SaveHostConfig(HostConfigDto dto);
    UiConfigDto GetUiConfig();
    UiConfigDto SaveUiConfig(UiConfigDto dto);
    string ResetApiKey();
}

public class ConfigService : IConfigService
{
    private readonly IConfigRepository _repo;
    private readonly IAppFolderInfo _appFolderInfo;

    public ConfigService(IConfigRepository repo, IAppFolderInfo appFolderInfo)
    {
        _repo = repo;
        _appFolderInfo = appFolderInfo;
        EnsureApiKeyExists();
    }

    private void EnsureApiKeyExists()
    {
        try
        {
            var existing = _repo.GetByKey("ApiKey");
            if (existing == null || string.IsNullOrWhiteSpace(existing.Value))
            {
                var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
                _repo.SetValue("ApiKey", key);
            }
        }
        catch
        {
            // Table may not be migrated yet during early DI construction
        }
    }

    public string ResetApiKey()
    {
        var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        _repo.SetValue("ApiKey", key);
        return key;
    }

    public ExtractionConfigDto GetExtractionConfig()
    {
        var d = _repo.GetAllDictionary();
        var passwordsRaw = d.GetValueOrDefault("ArchivePasswords", "");
        var passwords = passwordsRaw
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new ExtractionConfigDto
        {
            Id = 1,
            MaxConcurrentExtractions = int.TryParse(d.GetValueOrDefault("MaxConcurrentExtractions"), out var mce) ? mce : 2,
            BufferSizeKb = int.TryParse(d.GetValueOrDefault("BufferSizeKb"), out var bsk) ? bsk : 256,
            TempFolderName = d.GetValueOrDefault("TempFolderName", "_unpackerred"),
            FilePermissions = d.GetValueOrDefault("FilePermissions", "0644"),
            FolderPermissions = d.GetValueOrDefault("FolderPermissions", "0755"),
            ChownUid = d.GetValueOrDefault("ChownUid", Environment.GetEnvironmentVariable("PUID") ?? ""),
            ChownGid = d.GetValueOrDefault("ChownGid", Environment.GetEnvironmentVariable("PGID") ?? ""),
            ArchivePasswords = passwords,
            DeleteDelaySeconds = int.TryParse(d.GetValueOrDefault("DeleteDelaySeconds"), out var dds) ? dds : 300,
            MaxRetries = int.TryParse(d.GetValueOrDefault("MaxRetries"), out var mr) ? mr : 3
        };
    }

    public ExtractionConfigDto SaveExtractionConfig(ExtractionConfigDto dto)
    {
        _repo.SetValue("MaxConcurrentExtractions", Math.Clamp(dto.MaxConcurrentExtractions, 1, 16).ToString());
        _repo.SetValue("BufferSizeKb", Math.Clamp(dto.BufferSizeKb, 16, 16384).ToString());
        _repo.SetValue("TempFolderName", string.IsNullOrWhiteSpace(dto.TempFolderName) ? "_unpackerred" : dto.TempFolderName.Trim());
        _repo.SetValue("FilePermissions", string.IsNullOrWhiteSpace(dto.FilePermissions) ? "0644" : dto.FilePermissions.Trim());
        _repo.SetValue("FolderPermissions", string.IsNullOrWhiteSpace(dto.FolderPermissions) ? "0755" : dto.FolderPermissions.Trim());
        _repo.SetValue("ChownUid", dto.ChownUid?.Trim() ?? "");
        _repo.SetValue("ChownGid", dto.ChownGid?.Trim() ?? "");
        _repo.SetValue("ArchivePasswords", string.Join("\n", dto.ArchivePasswords ?? new List<string>()));
        _repo.SetValue("DeleteDelaySeconds", Math.Max(0, dto.DeleteDelaySeconds).ToString());
        _repo.SetValue("MaxRetries", Math.Clamp(dto.MaxRetries, 0, 20).ToString());
        return GetExtractionConfig();
    }

    public HostConfigDto GetHostConfig()
    {
        EnsureApiKeyExists();
        var d = _repo.GetAllDictionary();
        return new HostConfigDto
        {
            Id = 1,
            BindAddress = d.GetValueOrDefault("BindAddress", "*"),
            Port = int.TryParse(d.GetValueOrDefault("Port"), out var port) ? port : 8686,
            UrlBase = d.GetValueOrDefault("UrlBase", ""),
            EnableSsl = bool.TryParse(d.GetValueOrDefault("EnableSsl"), out var ssl) && ssl,
            SslPort = int.TryParse(d.GetValueOrDefault("SslPort"), out var sslPort) ? sslPort : 9898,
            AuthenticationMethod = d.GetValueOrDefault("AuthenticationMethod", "none"),
            AuthenticationRequired = d.GetValueOrDefault("AuthenticationRequired", "enabled"),
            Username = d.GetValueOrDefault("Username", "admin"),
            Password = "********",
            ApiKey = d.GetValueOrDefault("ApiKey", ""),
            LogLevel = d.GetValueOrDefault("LogLevel", "info"),
            AnalyticsEnabled = bool.TryParse(d.GetValueOrDefault("AnalyticsEnabled"), out var ae) && ae,
            ProxyEnabled = bool.TryParse(d.GetValueOrDefault("ProxyEnabled"), out var pe) && pe,
            ProxyHost = d.GetValueOrDefault("ProxyHost", ""),
            ProxyPort = int.TryParse(d.GetValueOrDefault("ProxyPort"), out var pp) ? pp : 8080,
            InstanceName = d.GetValueOrDefault("InstanceName", "Extractarr") is var instName && instName.Equals("Unpackarr", StringComparison.OrdinalIgnoreCase)
                ? "Extractarr"
                : instName
        };
    }

    public HostConfigDto SaveHostConfig(HostConfigDto dto)
    {
        _repo.SetValue("BindAddress", string.IsNullOrWhiteSpace(dto.BindAddress) ? "*" : dto.BindAddress.Trim());
        _repo.SetValue("Port", Math.Clamp(dto.Port, 1, 65535).ToString());
        _repo.SetValue("UrlBase", dto.UrlBase?.Trim() ?? "");
        _repo.SetValue("EnableSsl", dto.EnableSsl.ToString());
        _repo.SetValue("SslPort", Math.Clamp(dto.SslPort, 1, 65535).ToString());
        _repo.SetValue("AuthenticationMethod", dto.AuthenticationMethod ?? "none");
        _repo.SetValue("AuthenticationRequired", dto.AuthenticationRequired ?? "enabled");
        _repo.SetValue("Username", dto.Username ?? "admin");
        if (!string.IsNullOrWhiteSpace(dto.Password) && dto.Password != "********")
        {
            _repo.SetValue("PasswordHash", dto.Password);
        }

        var logLevel = string.IsNullOrWhiteSpace(dto.LogLevel) ? "info" : dto.LogLevel.ToLowerInvariant();
        _repo.SetValue("LogLevel", logLevel);
        ExtractarrLogger.SetLogLevel(logLevel, _appFolderInfo);

        _repo.SetValue("AnalyticsEnabled", dto.AnalyticsEnabled.ToString());
        _repo.SetValue("ProxyEnabled", dto.ProxyEnabled.ToString());
        _repo.SetValue("ProxyHost", dto.ProxyHost ?? "");
        _repo.SetValue("ProxyPort", dto.ProxyPort.ToString());
        _repo.SetValue("InstanceName", string.IsNullOrWhiteSpace(dto.InstanceName) ? "Extractarr" : dto.InstanceName.Trim());
        return GetHostConfig();
    }

    public UiConfigDto GetUiConfig()
    {
        var d = _repo.GetAllDictionary();
        return new UiConfigDto
        {
            Id = 1,
            Theme = d.GetValueOrDefault("Theme", "auto"),
            ShortDateFormat = d.GetValueOrDefault("ShortDateFormat", "MMM D YYYY"),
            LongDateFormat = d.GetValueOrDefault("LongDateFormat", "dddd, MMMM D YYYY"),
            TimeFormat = d.GetValueOrDefault("TimeFormat", "HH:mm"),
            ShowRelativeDates = !bool.TryParse(d.GetValueOrDefault("ShowRelativeDates"), out var srd) || srd,
            EnableColorImpairedMode = bool.TryParse(d.GetValueOrDefault("EnableColorImpairedMode"), out var ecim) && ecim,
            FirstDayOfWeek = int.TryParse(d.GetValueOrDefault("FirstDayOfWeek"), out var fdow) ? fdow : 0
        };
    }

    public UiConfigDto SaveUiConfig(UiConfigDto dto)
    {
        _repo.SetValue("Theme", dto.Theme ?? "auto");
        _repo.SetValue("ShortDateFormat", dto.ShortDateFormat ?? "MMM D YYYY");
        _repo.SetValue("LongDateFormat", dto.LongDateFormat ?? "dddd, MMMM D YYYY");
        _repo.SetValue("TimeFormat", dto.TimeFormat ?? "HH:mm");
        _repo.SetValue("ShowRelativeDates", dto.ShowRelativeDates.ToString());
        _repo.SetValue("EnableColorImpairedMode", dto.EnableColorImpairedMode.ToString());
        _repo.SetValue("FirstDayOfWeek", dto.FirstDayOfWeek.ToString());
        return GetUiConfig();
    }
}

