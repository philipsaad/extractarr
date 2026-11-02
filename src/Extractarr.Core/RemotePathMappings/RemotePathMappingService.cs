using System.Collections.Concurrent;
using Extractarr.Common.Disk;
using Extractarr.Core.Datastore;

namespace Extractarr.Core.RemotePathMappings;

public class RemotePathMapping : ModelBase
{
    public string Host { get; set; } = string.Empty; // Can match ArrInstance Name, Hostname, or BaseUrl
    public string RemotePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
}

public interface IRemotePathMappingRepository : IBasicRepository<RemotePathMapping>
{
}

public class RemotePathMappingRepository : BasicRepository<RemotePathMapping>, IRemotePathMappingRepository
{
    public RemotePathMappingRepository(IDatabase database) : base(database, "RemotePathMappings")
    {
    }
}

public interface IRemotePathMappingService
{
    string MapRemoteToLocal(string hostOrInstanceName, string baseUrl, string remotePath);
    IReadOnlyCollection<string> GetUnmappedRemotePaths();
    void RecordUnmappedPath(string instanceName, string remotePath);
    void ClearUnmappedPath(string remotePath);
}

public class RemotePathMappingService : IRemotePathMappingService
{
    private readonly IRemotePathMappingRepository _repository;
    private readonly ConcurrentDictionary<string, string> _unmappedPaths = new(StringComparer.OrdinalIgnoreCase);

    public RemotePathMappingService(IRemotePathMappingRepository repository)
    {
        _repository = repository;
    }

    public string MapRemoteToLocal(string hostOrInstanceName, string baseUrl, string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            return remotePath;
        }

        var mappings = _repository.All().ToList();
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedUri);
        var uriHost = parsedUri?.Host ?? string.Empty;

        foreach (var mapping in mappings)
        {
            var hostMatch = string.IsNullOrWhiteSpace(mapping.Host)
                            || mapping.Host == "*"
                            || mapping.Host.Equals(hostOrInstanceName, StringComparison.OrdinalIgnoreCase)
                            || mapping.Host.Equals(uriHost, StringComparison.OrdinalIgnoreCase)
                            || baseUrl.Contains(mapping.Host, StringComparison.OrdinalIgnoreCase);

            if (!hostMatch)
            {
                continue;
            }

            var normalizedRemote = NormalizeForComparison(mapping.RemotePath);
            var normalizedInput = NormalizeForComparison(remotePath);

            if (!string.IsNullOrEmpty(normalizedRemote) &&
                normalizedInput.StartsWith(normalizedRemote, StringComparison.OrdinalIgnoreCase) &&
                (normalizedInput.Length == normalizedRemote.Length || normalizedInput[normalizedRemote.Length] == '/'))
            {
                var relativeSubPath = normalizedInput[normalizedRemote.Length..].TrimStart('/', '\\');
                var mapped = OsPathTranslator.CombineForCurrentOs(mapping.LocalPath, relativeSubPath);

                ClearUnmappedPath(remotePath);
                return mapped;
            }
        }

        // Automatically translate between Windows and Linux/WSL/Docker paths for the current OS
        var autoTranslatedPath = OsPathTranslator.TranslateToCurrentOs(remotePath);
        if (File.Exists(autoTranslatedPath) || Directory.Exists(autoTranslatedPath))
        {
            ClearUnmappedPath(remotePath);
            return autoTranslatedPath;
        }

        if (File.Exists(remotePath) || Directory.Exists(remotePath))
        {
            ClearUnmappedPath(remotePath);
            return remotePath;
        }

        RecordUnmappedPath(hostOrInstanceName, remotePath);
        return autoTranslatedPath;
    }

    public IReadOnlyCollection<string> GetUnmappedRemotePaths() => _unmappedPaths.Values.ToList();

    public void RecordUnmappedPath(string instanceName, string remotePath)
    {
        _unmappedPaths[remotePath] = $"Unmapped remote path from {instanceName}: '{remotePath}'";
    }

    public void ClearUnmappedPath(string remotePath)
    {
        _unmappedPaths.TryRemove(remotePath, out _);
    }

    private static string NormalizeForComparison(string path)
    {
        return OsPathTranslator.NormalizeForCrossPlatformComparison(path);
    }
}


