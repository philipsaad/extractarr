using System.Runtime.InteropServices;

namespace Extractarr.Common.EnvironmentInfo;

public interface IAppFolderInfo
{
    string AppDataFolder { get; }
    string LogFolder { get; }
    string TempFolder { get; }
    string DatabasePath { get; }
}

public class AppFolderInfo : IAppFolderInfo
{
    public string AppDataFolder { get; }
    public string LogFolder { get; }
    public string TempFolder { get; }
    public string DatabasePath { get; }

    public AppFolderInfo(string? customAppDataPath = null)
    {
        var envDataDir = Environment.GetEnvironmentVariable("EXTRACTARR_DATA_DIR")
                         ?? Environment.GetEnvironmentVariable("UNPACKARR_DATA_DIR")
                         ?? Environment.GetEnvironmentVariable("APP_DATA");

        if (!string.IsNullOrWhiteSpace(customAppDataPath))
        {
            AppDataFolder = customAppDataPath;
        }
        else if (!string.IsNullOrWhiteSpace(envDataDir))
        {
            AppDataFolder = envDataDir;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && Directory.Exists("/config") && IsDirectoryWritable("/config"))
        {
            AppDataFolder = "/config";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && Directory.Exists("/var/lib/extractarr") && IsDirectoryWritable("/var/lib/extractarr"))
        {
            AppDataFolder = "/var/lib/extractarr";
        }
        else
        {
            var portableAppData = Path.Combine(AppContext.BaseDirectory, "App_Data");
            if (Directory.Exists(portableAppData) || IsDirectoryWritable(AppContext.BaseDirectory))
            {
                AppDataFolder = portableAppData;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var candidate = !string.IsNullOrWhiteSpace(programData)
                    ? Path.Combine(programData, "Extractarr")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Extractarr");
                AppDataFolder = candidate;
            }
            else
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                AppDataFolder = !string.IsNullOrWhiteSpace(home)
                    ? Path.Combine(home, ".config", "Extractarr")
                    : portableAppData;
            }
        }

        LogFolder = Path.Combine(AppDataFolder, "logs");
        TempFolder = Path.Combine(AppDataFolder, "temp");

        Directory.CreateDirectory(AppDataFolder);
        Directory.CreateDirectory(LogFolder);
        Directory.CreateDirectory(TempFolder);

        var legacyDbPath = Path.Combine(AppDataFolder, "unpackarr.db");
        var extractarrDbPath = Path.Combine(AppDataFolder, "extractarr.db");
        DatabasePath = File.Exists(legacyDbPath) && !File.Exists(extractarrDbPath)
            ? legacyDbPath
            : extractarrDbPath;
    }

    private static bool IsDirectoryWritable(string dirPath)
    {
        try
        {
            if (!Directory.Exists(dirPath))
            {
                return false;
            }

            var probeFile = Path.Combine(dirPath, $".extractarr_write_probe_{Guid.NewGuid():N}");
            using (File.Create(probeFile, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public static class OsInfo
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsOsx => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public static string OsName => RuntimeInformation.OSDescription;
    public static string Architecture => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    public static string RuntimeVersion => Environment.Version.ToString();
    public static DateTime StartTime { get; } = DateTime.UtcNow;

    public static bool IsDocker =>
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
        || (IsLinux && File.Exists("/.dockerenv"));

    public static string RuntimeMode
    {
        get
        {
            if (IsDocker) return "Docker";
            if (!Environment.UserInteractive)
            {
                return IsWindows ? "WindowsService" : "Systemd";
            }
            return "Console";
        }
    }
}

