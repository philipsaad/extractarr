using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NLog;
using Extractarr.Common.EnvironmentInfo;
using Extractarr.Common.Instrumentation;

namespace Extractarr.Common.Disk;

public class DiskSpaceInfo
{
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public long FreeSpace { get; set; }
    public long TotalSpace { get; set; }
}

public interface IDiskProvider
{
    List<DiskSpaceInfo> GetDiskSpace(IEnumerable<string>? additionalPaths = null);
    bool IsFileLocked(string filePath);
    bool HasSyncthingTempFiles(string directoryPath);
    void ApplyPermissions(string path, bool isDirectory, string octalMode, string? uid = null, string? gid = null);
    void DeleteFileSafe(string filePath);
}

public class DiskProvider : IDiskProvider
{
    private static readonly Logger Logger = ExtractarrLogger.GetLogger<DiskProvider>();

    // Virtual / ephemeral filesystem types ignored by Servarr/Sonarr on Linux & macOS
    private static readonly HashSet<string> IgnoredDriveFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "tmpfs", "devtmpfs", "ramfs", "proc", "sysfs", "cgroup", "cgroup2",
        "devpts", "mqueue", "hugetlbfs", "pstore", "securityfs", "debugfs",
        "tracefs", "configfs", "fusectl", "squashfs", "nsfs", "efivarfs",
        "bpf", "autofs", "binfmt_misc", "overlay", "iso9660", "udf", "devfs"
    };

    // System pseudo-mount paths ignored on Linux / macOS
    private static readonly Regex IgnoredUnixMountPathRegex = new(
        @"^/(dev|run|proc|sys|tmp|boot|snap|etc|var/lib/docker|var/lib/containers|var/lib/kubelet|private/var/vm)(/|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [DllImport("libc", SetLastError = true)]
    private static extern int chown(string pathname, uint owner, uint group);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailable,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

    public List<DiskSpaceInfo> GetDiskSpace(IEnumerable<string>? additionalPaths = null)
    {
        var results = new List<DiskSpaceInfo>();
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unixMountSources = GetUnixMountSources();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Network))
                {
                    continue;
                }

                try
                {
                    var rootPath = drive.RootDirectory.FullName;

                    if (!OsInfo.IsWindows)
                    {
                        if (IgnoredUnixMountPathRegex.IsMatch(rootPath))
                        {
                            continue;
                        }

                        var format = drive.DriveFormat;
                        if (!string.IsNullOrWhiteSpace(format) && IgnoredDriveFormats.Contains(format))
                        {
                            continue;
                        }
                    }

                    if (drive.TotalSize <= 0 || !seenRoots.Add(rootPath))
                    {
                        continue;
                    }

                    var label = drive.Name;
                    if (!OsInfo.IsWindows &&
                        unixMountSources.TryGetValue(rootPath, out var mountSource) &&
                        mountSource.StartsWith("//", StringComparison.Ordinal))
                    {
                        label = $"{rootPath} ({mountSource})";
                    }
                    else if (!string.IsNullOrWhiteSpace(drive.VolumeLabel) &&
                             !drive.VolumeLabel.Equals(drive.Name, StringComparison.OrdinalIgnoreCase) &&
                             !drive.VolumeLabel.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        label = $"{drive.Name} ({drive.VolumeLabel})";
                    }

                    results.Add(new DiskSpaceInfo
                    {
                        Path = rootPath,
                        Label = label,
                        FreeSpace = drive.AvailableFreeSpace,
                        TotalSpace = drive.TotalSize
                    });
                }
                catch
                {
                    // Ignore inaccessible virtual mounts
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to enumerate system drives");
        }

        // On Windows, also query any configured UNC paths (\\server\share) passed via additionalPaths
        if (OsInfo.IsWindows && additionalPaths != null)
        {
            foreach (var rawPath in additionalPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                try
                {
                    var root = Path.GetPathRoot(rawPath.Trim());
                    if (!string.IsNullOrWhiteSpace(root) &&
                        root.StartsWith(@"\\", StringComparison.Ordinal) &&
                        seenRoots.Add(root) &&
                        Directory.Exists(root) &&
                        GetDiskFreeSpaceEx(root, out var freeBytes, out var totalBytes, out _) &&
                        totalBytes > 0)
                    {
                        results.Add(new DiskSpaceInfo
                        {
                            Path = root,
                            Label = root,
                            FreeSpace = (long)Math.Min((ulong)long.MaxValue, freeBytes),
                            TotalSpace = (long)Math.Min((ulong)long.MaxValue, totalBytes)
                        });
                    }
                }
                catch
                {
                    // Ignore unreachable UNC shares
                }
            }
        }

        return results.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Dictionary<string, string> GetUnixMountSources()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (OsInfo.IsWindows || !File.Exists("/proc/mounts"))
        {
            return map;
        }

        try
        {
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var source = parts[0].Replace("\\040", " ");
                    var target = parts[1].Replace("\\040", " ");
                    map[target] = source;
                }
            }
        }
        catch
        {
            // Ignore errors reading /proc/mounts
        }

        return map;
    }

    public bool IsFileLocked(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool HasSyncthingTempFiles(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return false;
        }

        try
        {
            var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories);
            return files.Any(f =>
            {
                var name = Path.GetFileName(f);
                return name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                       || name.StartsWith(".syncthing.", StringComparison.OrdinalIgnoreCase)
                       || name.StartsWith("~syncthing~", StringComparison.OrdinalIgnoreCase)
                       || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Error checking for Syncthing temp files in {0}", directoryPath);
            return false;
        }
    }

    public void ApplyPermissions(string path, bool isDirectory, string octalMode, string? uid = null, string? gid = null)
    {
        if (OsInfo.IsWindows)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(octalMode))
            {
                var cleaned = octalMode.Trim();
                if (cleaned.Length == 4 && cleaned.StartsWith('0'))
                {
                    cleaned = cleaned[1..];
                }

                var modeInt = Convert.ToInt32(cleaned, 8);
                var unixMode = (UnixFileMode)modeInt;
                File.SetUnixFileMode(path, unixMode);
            }

            if (OsInfo.IsLinux && (!string.IsNullOrWhiteSpace(uid) || !string.IsNullOrWhiteSpace(gid)))
            {
                var ownerId = uint.TryParse(uid, out var parsedUid) ? parsedUid : uint.MaxValue;
                var groupId = uint.TryParse(gid, out var parsedGid) ? parsedGid : uint.MaxValue;
                chown(path, ownerId, groupId);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Unable to set Unix permissions/ownership ({0}) on {1}", octalMode, path);
        }
    }

    public void DeleteFileSafe(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            if (OsInfo.IsWindows)
            {
                var attrs = File.GetAttributes(filePath);
                if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
                }
            }

            File.Delete(filePath);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to delete file {0}", filePath);
            throw;
        }
    }
}

