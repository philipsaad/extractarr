using System.Text.RegularExpressions;
using Extractarr.Common.EnvironmentInfo;

namespace Extractarr.Common.Disk;

/// <summary>
/// Cross-platform path translator that detects the current OS (Windows vs Linux/macOS)
/// and automatically translates between Windows paths (drive letters, UNC shares, backslashes)
/// and Linux/WSL/Docker paths (/mnt/c, /c, /host_mnt/c, forward slashes).
/// </summary>
public static partial class OsPathTranslator
{
    // Matches Windows drive letter paths like "C:\foo\bar", "C:/foo/bar", or "C:"
    private static readonly Regex WindowsDriveRegex = new(
        @"^(?<drive>[a-zA-Z]):(?:[\\/](?<subpath>.*))?$",
        RegexOptions.Compiled);

    // Matches WSL / Docker / MSYS / Git Bash Linux mount paths like "/mnt/c/foo", "/host_mnt/c/foo", or "/c/foo"
    private static readonly Regex LinuxDriveMountRegex = new(
        @"^/(?:mnt/|host_mnt/)?(?<drive>[a-zA-Z])(?:/(?<subpath>.*))?$",
        RegexOptions.Compiled);

    // Matches UNC paths like "\\server\share\subpath" or "//server/share/subpath"
    private static readonly Regex UncPathRegex = new(
        @"^(?:\\\\|//)(?<server>[^\\/]+)[\\/](?<share>[^\\/]+)(?:[\\/](?<subpath>.*))?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Translates any Windows or Linux path into the native path format for the currently running OS.
    /// </summary>
    public static string TranslateToCurrentOs(string? rawPath)
    {
        return TranslatePath(rawPath, OsInfo.IsWindows);
    }

    /// <summary>
    /// Translates any Windows or Linux path into the target OS format (targetWindows = true for Windows, false for Linux/macOS).
    /// </summary>
    public static string TranslatePath(string? rawPath, bool targetWindows)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        var path = ExpandHomeAndEnvVars(rawPath.Trim(), targetWindows);

        return targetWindows
            ? TranslateToWindows(path)
            : TranslateToLinux(path);
    }

    /// <summary>
    /// Combines a base path and relative sub-path and translates the result for the current OS.
    /// </summary>
    public static string CombineForCurrentOs(string basePath, string relativeSubPath)
    {
        return CombineForOs(basePath, relativeSubPath, OsInfo.IsWindows);
    }

    public static string CombineForOs(string basePath, string relativeSubPath, bool targetWindows)
    {
        var translatedBase = TranslatePath(basePath, targetWindows);
        if (string.IsNullOrWhiteSpace(relativeSubPath))
        {
            return translatedBase;
        }

        var sep = targetWindows ? '\\' : '/';
        var cleanSub = relativeSubPath
            .Replace('/', sep)
            .Replace('\\', sep)
            .TrimStart(sep);

        if (string.IsNullOrEmpty(cleanSub))
        {
            return translatedBase;
        }

        var cleanBase = translatedBase.TrimEnd('/', '\\');
        if (targetWindows && cleanBase.Length == 2 && cleanBase[1] == ':')
        {
            return $"{cleanBase}\\{cleanSub}";
        }

        return $"{cleanBase}{sep}{cleanSub}";
    }

    /// <summary>
    /// Produces an OS-neutral canonical path string for prefix comparison across Windows and Linux paths.
    /// For example, both "C:\Users\Test\Downloads" and "/mnt/c/Users/Test/Downloads" normalize to "/mnt/c/Users/Test/Downloads".
    /// </summary>
    public static string NormalizeForCrossPlatformComparison(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        var trimmed = rawPath.Trim();

        var winMatch = WindowsDriveRegex.Match(trimmed);
        if (winMatch.Success)
        {
            var drive = winMatch.Groups["drive"].Value.ToLowerInvariant();
            var sub = winMatch.Groups["subpath"].Value.Replace('\\', '/').Trim('/');
            return string.IsNullOrEmpty(sub) ? $"/mnt/{drive}" : $"/mnt/{drive}/{sub}";
        }

        var linuxMountMatch = LinuxDriveMountRegex.Match(trimmed.Replace('\\', '/'));
        if (linuxMountMatch.Success)
        {
            var drive = linuxMountMatch.Groups["drive"].Value.ToLowerInvariant();
            var sub = linuxMountMatch.Groups["subpath"].Value.Replace('\\', '/').Trim('/');
            return string.IsNullOrEmpty(sub) ? $"/mnt/{drive}" : $"/mnt/{drive}/{sub}";
        }

        return trimmed.Replace('\\', '/').TrimEnd('/');
    }

    /// <summary>
    /// Returns the default local downloads directory appropriate for the current OS.
    /// </summary>
    public static string GetDefaultDownloadsDirectory()
    {
        if (OsInfo.IsWindows)
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                return Path.Combine(userProfile, "Downloads");
            }
            return @"C:\Downloads";
        }

        if (Directory.Exists("/downloads"))
        {
            return "/downloads";
        }

        var unixHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(unixHome))
        {
            var candidate = Path.Combine(unixHome, "Downloads");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return "/downloads";
    }

    private static string TranslateToWindows(string path)
    {
        // 1. Check UNC path ("\\server\share\..." or "//server/share/...")
        var uncMatch = UncPathRegex.Match(path);
        if (uncMatch.Success)
        {
            var server = uncMatch.Groups["server"].Value;
            var share = uncMatch.Groups["share"].Value;
            var sub = uncMatch.Groups["subpath"].Value.Replace('/', '\\').Trim('\\');
            return string.IsNullOrEmpty(sub)
                ? $@"\\{server}\{share}"
                : $@"\\{server}\{share}\{sub}";
        }

        // 2. Check Windows drive path ("C:/foo/bar" or "C:\foo\bar")
        var winMatch = WindowsDriveRegex.Match(path);
        if (winMatch.Success)
        {
            var drive = char.ToUpperInvariant(winMatch.Groups["drive"].Value[0]);
            var sub = winMatch.Groups["subpath"].Value.Replace('/', '\\').Trim('\\');
            return string.IsNullOrEmpty(sub) ? $@"{drive}:\" : $@"{drive}:\{sub}";
        }

        // 3. Check Linux/WSL/Docker drive mount ("/mnt/c/Users/...", "/host_mnt/c/...", "/c/Users/...")
        var normalizedSlashes = path.Replace('\\', '/');
        var linuxMountMatch = LinuxDriveMountRegex.Match(normalizedSlashes);
        if (linuxMountMatch.Success)
        {
            var drive = char.ToUpperInvariant(linuxMountMatch.Groups["drive"].Value[0]);
            var sub = linuxMountMatch.Groups["subpath"].Value.Replace('/', '\\').Trim('\\');
            return string.IsNullOrEmpty(sub) ? $@"{drive}:\" : $@"{drive}:\{sub}";
        }

        // 4. Rooted Unix path on Windows (e.g. "/downloads/Show.S01E01" or "/data/torrents")
        if (normalizedSlashes.StartsWith('/'))
        {
            var trimmedRel = normalizedSlashes.TrimStart('/').Replace('/', '\\');

            // If it starts with "downloads" or "Downloads", check if it exists inside the Windows user's Downloads folder
            if (trimmedRel.Equals("downloads", StringComparison.OrdinalIgnoreCase) ||
                trimmedRel.StartsWith(@"downloads\", StringComparison.OrdinalIgnoreCase))
            {
                var subAfterDownloads = trimmedRel.Length > "downloads".Length
                    ? trimmedRel["downloads".Length..].TrimStart('\\')
                    : string.Empty;

                var userDownloads = GetDefaultDownloadsDirectory();
                var userDownloadsCandidate = string.IsNullOrEmpty(subAfterDownloads)
                    ? userDownloads
                    : Path.Combine(userDownloads, subAfterDownloads);

                if (OsInfo.IsWindows && (File.Exists(userDownloadsCandidate) || Directory.Exists(userDownloadsCandidate)))
                {
                    return userDownloadsCandidate;
                }
            }

            var sysRoot = OsInfo.IsWindows
                ? (Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\")
                : @"C:\";
            if (!sysRoot.EndsWith('\\'))
            {
                sysRoot += "\\";
            }

            return string.IsNullOrEmpty(trimmedRel)
                ? sysRoot
                : $"{sysRoot}{trimmedRel}";
        }

        // 5. Relative path: normalize slashes to Windows backslash
        return path.Replace('/', '\\');
    }

    private static string TranslateToLinux(string path)
    {
        // 1. Check Windows drive path ("C:\Users\..." or "C:/Users/...")
        var winMatch = WindowsDriveRegex.Match(path);
        if (winMatch.Success)
        {
            var drive = winMatch.Groups["drive"].Value.ToLowerInvariant();
            var sub = winMatch.Groups["subpath"].Value.Replace('\\', '/').Trim('/');

            // Probe common Linux mount prefixes if running on Linux
            var mountPrefixes = new[] { $"/mnt/{drive}", $"/{drive}", $"/host_mnt/{drive}", $"/media/{drive}" };
            if (!OsInfo.IsWindows)
            {
                foreach (var prefix in mountPrefixes)
                {
                    var candidate = string.IsNullOrEmpty(sub) ? prefix : $"{prefix}/{sub}";
                    if (File.Exists(candidate) || Directory.Exists(candidate) || Directory.Exists(prefix))
                    {
                        return candidate;
                    }
                }
            }

            return string.IsNullOrEmpty(sub) ? $"/mnt/{drive}" : $"/mnt/{drive}/{sub}";
        }

        // 2. Check UNC path ("\\server\share\subpath")
        var uncMatch = UncPathRegex.Match(path);
        if (uncMatch.Success)
        {
            var server = uncMatch.Groups["server"].Value;
            var share = uncMatch.Groups["share"].Value;
            var sub = uncMatch.Groups["subpath"].Value.Replace('\\', '/').Trim('/');

            if (!OsInfo.IsWindows)
            {
                // Check /proc/mounts first for active CIFS/SMB/NFS mount matching //server/share
                var mountedTarget = TryResolveLinuxMountTarget(server, share);
                if (!string.IsNullOrEmpty(mountedTarget))
                {
                    return string.IsNullOrEmpty(sub) ? mountedTarget : $"{mountedTarget}/{sub}";
                }

                var candidateMounts = new[]
                {
                    $"/media/{share}",
                    $"/mnt/{share}",
                    $"/media/{server}/{share}",
                    $"/mnt/{server}/{share}"
                };

                foreach (var candidate in candidateMounts)
                {
                    if (Directory.Exists(candidate))
                    {
                        return string.IsNullOrEmpty(sub) ? candidate : $"{candidate}/{sub}";
                    }
                }
            }

            return string.IsNullOrEmpty(sub)
                ? $"/mnt/{server}/{share}"
                : $"/mnt/{server}/{share}/{sub}";
        }

        // 3. Standard Linux path (possibly containing stray backslashes)
        var normalized = path.Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    private static string ExpandHomeAndEnvVars(string path, bool targetWindows)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);

        if (expanded.StartsWith("~/", StringComparison.Ordinal) ||
            expanded.StartsWith(@"~\", StringComparison.Ordinal) ||
            expanded == "~")
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                var rest = expanded.Length > 1 ? expanded[1..].TrimStart('/', '\\') : string.Empty;
                return string.IsNullOrEmpty(rest)
                    ? home
                    : (targetWindows ? $@"{home.TrimEnd('\\', '/')}\{rest}" : $"{home.TrimEnd('/', '\\')}/{rest}");
            }
        }

        return expanded;
    }

    private static string? TryResolveLinuxMountTarget(string server, string share)
    {
        if (OsInfo.IsWindows || !File.Exists("/proc/mounts"))
        {
            return null;
        }

        var expectedSource = $"//{server}/{share}";
        try
        {
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var source = parts[0].Replace("\\040", " ").TrimEnd('/');
                    var target = parts[1].Replace("\\040", " ").TrimEnd('/');
                    if (source.Equals(expectedSource, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(target))
                    {
                        return target;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors reading /proc/mounts
        }

        return null;
    }
}

