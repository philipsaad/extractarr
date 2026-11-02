using System.Diagnostics;
using System.Text.RegularExpressions;
using NLog;
using Extractarr.Common.Disk;
using Extractarr.Common.Instrumentation;
using Extractarr.Core.Configuration;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Extractarr.Core.Extraction;

public class ExtractionProgressReport
{
    public long ExtractedBytes { get; set; }
    public long TotalBytes { get; set; }
    public double ProgressPercent { get; set; }
    public long SpeedBytesPerSec { get; set; }
    public int EtaSeconds { get; set; }
}

public class ExtractionOutcome
{
    public bool Success { get; set; }
    public List<string> ExtractedFilePaths { get; set; } = new();
    public long TotalArchiveBytes { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }
}

public interface ISharpCompressExtractor
{
    List<string> DiscoverPrimaryArchives(string path, bool recursive = true);
    string DetectArchiveType(string archiveFilePath);
    Task<ExtractionOutcome> ExtractArchiveAsync(
        string archiveFilePath,
        string destinationDirectory,
        string? manualPassword,
        Action<ExtractionProgressReport>? onProgress,
        CancellationToken cancellationToken = default);
}

public class SharpCompressExtractor : ISharpCompressExtractor
{
    private readonly IConfigService _configService;
    private readonly IDiskProvider _diskProvider;
    private static readonly Logger Logger = ExtractarrLogger.GetLogger<SharpCompressExtractor>();

    private static readonly Regex MultiPartRarContinuationRegex = new(
        @"(\.part(?!0*1\.rar$)\d+\.rar$|\.r\d{2,3}$|\.s\d{2,3}$|\.t\d{2,3}$|\.\d{3}$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".rar", ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".r00"
    };

    public SharpCompressExtractor(IConfigService configService, IDiskProvider diskProvider)
    {
        _configService = configService;
        _diskProvider = diskProvider;
    }

    public List<string> DiscoverPrimaryArchives(string path, bool recursive = true)
    {
        var results = new List<string>();

        if (File.Exists(path))
        {
            if (IsPrimaryArchiveFile(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path }))
            {
                results.Add(path);
            }
            return results;
        }

        if (!Directory.Exists(path))
        {
            return results;
        }

        var config = _configService.GetExtractionConfig();
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var allFiles = Directory.EnumerateFiles(path, "*", searchOption)
            .Where(f =>
            {
                var rel = Path.GetRelativePath(path, f);
                return !rel.StartsWith(config.TempFolderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                       && !rel.Contains(Path.DirectorySeparatorChar + config.TempFolderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        var fileSet = new HashSet<string>(allFiles, StringComparer.OrdinalIgnoreCase);

        foreach (var file in allFiles)
        {
            if (IsPrimaryArchiveFile(file, fileSet))
            {
                results.Add(file);
            }
        }

        return results;
    }

    private static bool IsPrimaryArchiveFile(string filePath, HashSet<string> allFilesInScope)
    {
        var fileName = Path.GetFileName(filePath);
        var ext = Path.GetExtension(filePath);

        if (!SupportedExtensions.Contains(ext))
        {
            return false;
        }

        // Skip continuation parts like .part02.rar, .r01, .r02
        if (MultiPartRarContinuationRegex.IsMatch(fileName))
        {
            return false;
        }

        // If it's a .r00 file, only treat it as primary if a corresponding .rar file does NOT exist
        if (ext.Equals(".r00", StringComparison.OrdinalIgnoreCase))
        {
            var rarCandidate = Path.ChangeExtension(filePath, ".rar");
            if (allFilesInScope.Contains(rarCandidate) || File.Exists(rarCandidate))
            {
                return false;
            }
        }

        return true;
    }

    public string DetectArchiveType(string archiveFilePath)
    {
        var lower = archiveFilePath.ToLowerInvariant();
        if (lower.EndsWith(".rar") || lower.EndsWith(".r00")) return "rar";
        if (lower.EndsWith(".7z")) return "7z";
        if (lower.EndsWith(".zip")) return "zip";
        if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz")) return "tgz";
        if (lower.EndsWith(".tar")) return "tar";
        if (lower.EndsWith(".gz")) return "gzip";
        return Path.GetExtension(archiveFilePath).TrimStart('.').ToLowerInvariant();
    }

    public async Task<ExtractionOutcome> ExtractArchiveAsync(
        string archiveFilePath,
        string destinationDirectory,
        string? manualPassword,
        Action<ExtractionProgressReport>? onProgress,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var config = _configService.GetExtractionConfig();
        var tempFolderPath = Path.Combine(destinationDirectory, config.TempFolderName, $"job_{Guid.NewGuid():N}");

        try
        {
            if (!File.Exists(archiveFilePath))
            {
                return new ExtractionOutcome
                {
                    Success = false,
                    ErrorMessage = $"Archive file not found: {archiveFilePath}"
                };
            }

            Directory.CreateDirectory(destinationDirectory);
            Directory.CreateDirectory(tempFolderPath);

            var archiveInfo = new FileInfo(archiveFilePath);
            var totalArchiveBytes = archiveInfo.Length;

            // Build password candidate list: ManualPassword -> null (no password) -> configured password list
            var passwordCandidates = new List<string?>();
            if (!string.IsNullOrWhiteSpace(manualPassword))
            {
                passwordCandidates.Add(manualPassword);
            }
            passwordCandidates.Add(null);
            foreach (var pw in config.ArchivePasswords.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                if (!passwordCandidates.Contains(pw))
                {
                    passwordCandidates.Add(pw);
                }
            }

            Exception? lastException = null;
            var extractedSuccessfully = false;

            foreach (var password in passwordCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Clean temp directory before each attempt
                    if (Directory.Exists(tempFolderPath))
                    {
                        Directory.Delete(tempFolderPath, true);
                    }
                    Directory.CreateDirectory(tempFolderPath);

                    await Task.Run(() => ExtractSinglePass(
                        archiveFilePath,
                        tempFolderPath,
                        password,
                        config.BufferSizeKb * 1024,
                        totalArchiveBytes,
                        onProgress,
                        cancellationToken), cancellationToken);

                    extractedSuccessfully = true;
                    lastException = null;
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (IsPasswordOrEncryptionError(ex))
                {
                    lastException = ex;
                    Logger.Debug("Password candidate failed for {0}, trying next password...", archiveFilePath);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    // Also try remaining passwords if a password hasn't been tried yet
                    if (password == null && passwordCandidates.Count > 1)
                    {
                        continue;
                    }
                    break;
                }
            }

            if (!extractedSuccessfully)
            {
                sw.Stop();
                return new ExtractionOutcome
                {
                    Success = false,
                    TotalArchiveBytes = totalArchiveBytes,
                    DurationMs = sw.ElapsedMilliseconds,
                    ErrorMessage = lastException?.Message ?? "Archive extraction failed after trying all configured passwords.",
                    ErrorStackTrace = lastException?.ToString()
                };
            }

            // Recursive extraction for nested archives inside tempFolderPath (up to 3 levels deep)
            for (var depth = 0; depth < 3; depth++)
            {
                var nestedArchives = DiscoverPrimaryArchives(tempFolderPath, recursive: true);
                if (nestedArchives.Count == 0)
                {
                    break;
                }

                foreach (var nested in nestedArchives)
                {
                    Logger.Info("Detected nested archive '{0}' at depth {1}, extracting recursively...", Path.GetFileName(nested), depth + 1);
                    var nestedTargetDir = Path.GetDirectoryName(nested) ?? tempFolderPath;
                    ExtractSinglePass(
                        nested,
                        nestedTargetDir,
                        manualPassword,
                        config.BufferSizeKb * 1024,
                        new FileInfo(nested).Length,
                        null,
                        cancellationToken);

                    // Delete nested archive parts inside temp folder so only unpacked payload moves to destination
                    _diskProvider.DeleteFileSafe(nested);
                }
            }

            // Move unpacked files from tempFolderPath into destinationDirectory and apply permissions
            var finalMovedFiles = new List<string>();
            var unpackedFiles = Directory.GetFiles(tempFolderPath, "*", SearchOption.AllDirectories);

            foreach (var tempFile in unpackedFiles)
            {
                var relativePath = Path.GetRelativePath(tempFolderPath, tempFile);
                var finalPath = Path.Combine(destinationDirectory, relativePath);
                var finalDir = Path.GetDirectoryName(finalPath)!;

                if (!Directory.Exists(finalDir))
                {
                    Directory.CreateDirectory(finalDir);
                    _diskProvider.ApplyPermissions(finalDir, true, config.FolderPermissions, config.ChownUid, config.ChownGid);
                }

                if (File.Exists(finalPath))
                {
                    _diskProvider.DeleteFileSafe(finalPath);
                }

                File.Move(tempFile, finalPath);
                _diskProvider.ApplyPermissions(finalPath, false, config.FilePermissions, config.ChownUid, config.ChownGid);
                finalMovedFiles.Add(finalPath);
            }

            // Clean up temporary directory
            try
            {
                var rootTempDir = Path.Combine(destinationDirectory, config.TempFolderName);
                if (Directory.Exists(tempFolderPath))
                {
                    Directory.Delete(tempFolderPath, true);
                }
                if (Directory.Exists(rootTempDir) && !Directory.EnumerateFileSystemEntries(rootTempDir).Any())
                {
                    Directory.Delete(rootTempDir, true);
                }
            }
            catch (Exception cleanupEx)
            {
                Logger.Debug(cleanupEx, "Non-fatal error removing temporary folder {0}", tempFolderPath);
            }

            sw.Stop();
            onProgress?.Invoke(new ExtractionProgressReport
            {
                ExtractedBytes = totalArchiveBytes,
                TotalBytes = totalArchiveBytes,
                ProgressPercent = 100.0,
                SpeedBytesPerSec = sw.ElapsedMilliseconds > 0 ? (totalArchiveBytes * 1000) / Math.Max(1, sw.ElapsedMilliseconds) : totalArchiveBytes,
                EtaSeconds = 0
            });

            return new ExtractionOutcome
            {
                Success = true,
                ExtractedFilePaths = finalMovedFiles,
                TotalArchiveBytes = totalArchiveBytes,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException)
        {
            CleanupTempDirQuietly(destinationDirectory, tempFolderPath, config.TempFolderName);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            CleanupTempDirQuietly(destinationDirectory, tempFolderPath, config.TempFolderName);
            Logger.Error(ex, "Error extracting archive {0}", archiveFilePath);
            return new ExtractionOutcome
            {
                Success = false,
                DurationMs = sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message,
                ErrorStackTrace = ex.ToString()
            };
        }
    }

    private static void ExtractSinglePass(
        string archiveFilePath,
        string tempFolderPath,
        string? password,
        int bufferSize,
        long fallbackTotalBytes,
        Action<ExtractionProgressReport>? onProgress,
        CancellationToken cancellationToken)
    {
        var readerOptions = new ReaderOptions
        {
            Password = password,
            LookForHeader = true
        };

        var sw = Stopwatch.StartNew();
        long lastReportedMs = 0;
        long extractedBytes = 0;

        // Try ArchiveFactory first (supports random-access ZIP, 7z, RAR, multi-part RAR)
        try
        {
            using var archive = ArchiveFactory.Open(archiveFilePath, readerOptions);
            var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
            var totalUncompressedBytes = Math.Max(1L, entries.Sum(e => e.Size > 0 ? e.Size : 0));
            if (totalUncompressedBytes <= 1)
            {
                totalUncompressedBytes = Math.Max(1L, fallbackTotalBytes);
            }

            var buffer = new byte[Math.Clamp(bufferSize, 16384, 4 * 1024 * 1024)];

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                var safeRelPath = SanitizeEntryPath(entry.Key);
                var targetFile = Path.Combine(tempFolderPath, safeRelPath);
                var targetDir = Path.GetDirectoryName(targetFile)!;
                Directory.CreateDirectory(targetDir);

                using var entryStream = entry.OpenEntryStream();
                using var outStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length);

                int bytesRead;
                while ((bytesRead = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    outStream.Write(buffer, 0, bytesRead);
                    extractedBytes += bytesRead;

                    var elapsedMs = sw.ElapsedMilliseconds;
                    if (onProgress != null && elapsedMs - lastReportedMs >= 200)
                    {
                        lastReportedMs = elapsedMs;
                        ReportProgress(onProgress, extractedBytes, totalUncompressedBytes, elapsedMs);
                    }
                }
            }

            return;
        }
        catch (Exception ex) when (IsPasswordOrEncryptionError(ex) || ex is OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fallback to stream ReaderFactory (for .tar.gz, .tgz, .gz streams)
        }

        using var fileStream = File.OpenRead(archiveFilePath);
        using var reader = ReaderFactory.Open(fileStream, readerOptions);
        var streamBuffer = new byte[Math.Clamp(bufferSize, 16384, 4 * 1024 * 1024)];

        while (reader.MoveToNextEntry())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.Entry.IsDirectory || string.IsNullOrWhiteSpace(reader.Entry.Key))
            {
                continue;
            }

            var safeRelPath = SanitizeEntryPath(reader.Entry.Key);
            var targetFile = Path.Combine(tempFolderPath, safeRelPath);
            var targetDir = Path.GetDirectoryName(targetFile)!;
            Directory.CreateDirectory(targetDir);

            using var entryStream = reader.OpenEntryStream();
            using var outStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, streamBuffer.Length);

            int bytesRead;
            while ((bytesRead = entryStream.Read(streamBuffer, 0, streamBuffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                outStream.Write(streamBuffer, 0, bytesRead);
                extractedBytes += bytesRead;

                var elapsedMs = sw.ElapsedMilliseconds;
                if (onProgress != null && elapsedMs - lastReportedMs >= 200)
                {
                    lastReportedMs = elapsedMs;
                    ReportProgress(onProgress, extractedBytes, Math.Max(extractedBytes, fallbackTotalBytes), elapsedMs);
                }
            }
        }
    }

    private static void ReportProgress(Action<ExtractionProgressReport> onProgress, long extractedBytes, long totalBytes, long elapsedMs)
    {
        var pct = totalBytes > 0 ? Math.Clamp((extractedBytes * 100.0) / totalBytes, 0.0, 99.9) : 50.0;
        var speed = elapsedMs > 0 ? (extractedBytes * 1000L) / elapsedMs : 0L;
        var remainingBytes = Math.Max(0L, totalBytes - extractedBytes);
        var eta = speed > 0 ? (int)Math.Ceiling((double)remainingBytes / speed) : 0;

        onProgress(new ExtractionProgressReport
        {
            ExtractedBytes = extractedBytes,
            TotalBytes = totalBytes,
            ProgressPercent = Math.Round(pct, 1),
            SpeedBytesPerSec = speed,
            EtaSeconds = eta
        });
    }

    private static string SanitizeEntryPath(string entryKey)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var normalized = entryKey.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var parts = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != ".." && p != ".")
            .Select(segment =>
            {
                var cleaned = new string(segment.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray()).TrimEnd(' ', '.');
                return string.IsNullOrWhiteSpace(cleaned) ? "_" : cleaned;
            })
            .ToArray();
        return parts.Length == 0 ? "extracted_file" : Path.Combine(parts);
    }

    private static bool IsPasswordOrEncryptionError(Exception ex)
    {
        var msg = ex.ToString();
        return ex is CryptographicException
               || msg.Contains("password", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("encrypted", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("cipher", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("CRC", StringComparison.OrdinalIgnoreCase);
    }

    private static void CleanupTempDirQuietly(string destinationDirectory, string tempFolderPath, string tempFolderName)
    {
        try
        {
            if (Directory.Exists(tempFolderPath))
            {
                Directory.Delete(tempFolderPath, true);
            }
            var rootTemp = Path.Combine(destinationDirectory, tempFolderName);
            if (Directory.Exists(rootTemp) && !Directory.EnumerateFileSystemEntries(rootTemp).Any())
            {
                Directory.Delete(rootTemp, true);
            }
        }
        catch
        {
            // Ignore cleanup failure
        }
    }
}
