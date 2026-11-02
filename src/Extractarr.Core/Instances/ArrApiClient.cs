using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;
using Extractarr.Common.Instrumentation;

namespace Extractarr.Core.Instances;

public class ArrSystemStatusDto
{
    [JsonPropertyName("appName")]
    public string? AppName { get; set; }

    [JsonPropertyName("instanceName")]
    public string? InstanceName { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public class ArrStatusMessageDto
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("messages")]
    public List<string>? Messages { get; set; }
}

public class ArrQueueRecordDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("downloadId")]
    public string? DownloadId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("trackedDownloadStatus")]
    public string? TrackedDownloadStatus { get; set; }

    [JsonPropertyName("trackedDownloadState")]
    public string? TrackedDownloadState { get; set; }

    [JsonPropertyName("statusMessages")]
    public List<ArrStatusMessageDto>? StatusMessages { get; set; }

    [JsonPropertyName("outputPath")]
    public string? OutputPath { get; set; }

    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }

    [JsonPropertyName("downloadClient")]
    public string? DownloadClient { get; set; }

    [JsonPropertyName("size")]
    public double Size { get; set; }

    [JsonPropertyName("sizeleft")]
    public double SizeLeft { get; set; }

    public bool IsCompletedAndReadyForExtraction()
    {
        var st = Status?.ToLowerInvariant() ?? "";
        var tds = TrackedDownloadState?.ToLowerInvariant() ?? "";
        var isDownloadDone = st == "completed" || (Size > 0 && SizeLeft <= 0.01);

        if (!isDownloadDone)
        {
            return false;
        }

        // If already imported, don't extract again
        if (tds == "imported")
        {
            return false;
        }

        return true;
    }
}

public class ArrQueuePageDto
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalRecords")]
    public int TotalRecords { get; set; }

    [JsonPropertyName("records")]
    public List<ArrQueueRecordDto> Records { get; set; } = new();
}

public class ArrConnectionTestResult
{
    public bool IsValid { get; set; }
    public string? Version { get; set; }
    public string? AppName { get; set; }
    public string Message { get; set; } = string.Empty;
}

public interface IArrApiClient
{
    Task<ArrConnectionTestResult> TestConnectionAsync(ArrInstance instance, CancellationToken cancellationToken = default);
    Task<List<ArrQueueRecordDto>> GetQueueAsync(ArrInstance instance, CancellationToken cancellationToken = default);
    Task<bool> TriggerRefreshMonitoredDownloadsAsync(ArrInstance instance, CancellationToken cancellationToken = default);
}

public class ArrApiClient : IArrApiClient
{
    private static readonly Logger Logger = ExtractarrLogger.GetLogger<ArrApiClient>();

    private static string GetApiVersionPrefix(string instanceType)
    {
        return instanceType.Equals("Lidarr", StringComparison.OrdinalIgnoreCase) ||
               instanceType.Equals("Readarr", StringComparison.OrdinalIgnoreCase)
            ? "api/v1"
            : "api/v3";
    }

    private static HttpClient CreateClient(ArrInstance instance)
    {
        var handler = new HttpClientHandler();
        if (!instance.VerifySsl)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(instance.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(Math.Clamp(instance.TimeoutSeconds, 3, 120))
        };

        client.DefaultRequestHeaders.Remove("X-Api-Key");
        client.DefaultRequestHeaders.Add("X-Api-Key", instance.ApiKey.Trim());
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Unpackarr/1.0.0.0");
        return client;
    }

    public async Task<ArrConnectionTestResult> TestConnectionAsync(ArrInstance instance, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instance.BaseUrl))
        {
            return new ArrConnectionTestResult { IsValid = false, Message = "Base URL is required." };
        }

        if (string.IsNullOrWhiteSpace(instance.ApiKey))
        {
            return new ArrConnectionTestResult { IsValid = false, Message = "API Key is required." };
        }

        if (!Uri.TryCreate(instance.BaseUrl, UriKind.Absolute, out _))
        {
            return new ArrConnectionTestResult { IsValid = false, Message = "Base URL is not a valid absolute URL." };
        }

        try
        {
            using var client = CreateClient(instance);
            var prefix = GetApiVersionPrefix(instance.InstanceType);
            var response = await client.GetAsync($"{prefix}/system/status", cancellationToken);

            // Fallback to api/v3 if api/v1 returned 404 or vice versa
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var altPrefix = prefix == "api/v3" ? "api/v1" : "api/v3";
                response = await client.GetAsync($"{altPrefix}/system/status", cancellationToken);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return new ArrConnectionTestResult
                {
                    IsValid = false,
                    Message = $"Authentication failed (HTTP {(int)response.StatusCode}): Invalid API Key."
                };
            }

            response.EnsureSuccessStatusCode();

            var status = await response.Content.ReadFromJsonAsync<ArrSystemStatusDto>(cancellationToken: cancellationToken);
            var version = status?.Version ?? "5.0.0";
            var appName = status?.AppName ?? instance.InstanceType;

            return new ArrConnectionTestResult
            {
                IsValid = true,
                Version = version,
                AppName = appName,
                Message = $"Connected to {appName} v{version}"
            };
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Connection test failed for {0} ({1})", instance.Name, instance.BaseUrl);
            return new ArrConnectionTestResult
            {
                IsValid = false,
                Message = $"Connection failed: {ex.Message}"
            };
        }
    }

    public async Task<List<ArrQueueRecordDto>> GetQueueAsync(ArrInstance instance, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(instance);
        var prefix = GetApiVersionPrefix(instance.InstanceType);

        var url = $"{prefix}/queue?page=1&pageSize=250&includeUnknownSeriesItems=true&includeUnknownMovieItems=true";
        var response = await client.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var altPrefix = prefix == "api/v3" ? "api/v1" : "api/v3";
            response = await client.GetAsync($"{altPrefix}/queue?page=1&pageSize=250", cancellationToken);
        }

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var page = JsonSerializer.Deserialize<ArrQueuePageDto>(content);
            if (page?.Records != null)
            {
                return page.Records;
            }
        }
        catch
        {
            // Some endpoints return a direct JSON array
            var list = JsonSerializer.Deserialize<List<ArrQueueRecordDto>>(content);
            if (list != null)
            {
                return list;
            }
        }

        return new List<ArrQueueRecordDto>();
    }

    public async Task<bool> TriggerRefreshMonitoredDownloadsAsync(ArrInstance instance, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(instance);
            var prefix = GetApiVersionPrefix(instance.InstanceType);
            var payload = new { name = "RefreshMonitoredDownloads" };

            var response = await client.PostAsJsonAsync($"{prefix}/command", payload, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var altPrefix = prefix == "api/v3" ? "api/v1" : "api/v3";
                response = await client.PostAsJsonAsync($"{altPrefix}/command", payload, cancellationToken);
            }

            response.EnsureSuccessStatusCode();
            Logger.Info("Sent RefreshMonitoredDownloads command to {0} ({1})", instance.Name, instance.InstanceType);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to trigger RefreshMonitoredDownloads on {0}", instance.Name);
            return false;
        }
    }
}

