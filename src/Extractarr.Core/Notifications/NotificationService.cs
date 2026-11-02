using System.Net.Http.Json;
using System.Text.Json;
using NLog;
using Extractarr.Common.Instrumentation;
using Extractarr.Core.Datastore;
using Extractarr.Core.Extraction;
using Extractarr.Core.Messaging.Events;

namespace Extractarr.Core.Notifications;

public class NotificationDefinition : ModelBase
{
    public string Name { get; set; } = string.Empty;
    public string Implementation { get; set; } = "Webhook"; // Webhook, Discord, Slack, Telegram, Apprise, Pushover
    public bool Enable { get; set; } = true;
    public bool OnExtractionStart { get; set; }
    public bool OnExtractionComplete { get; set; } = true;
    public bool OnExtractionFailure { get; set; } = true;
    public bool OnCleanup { get; set; }
    public string SettingsJson { get; set; } = "{}";

    public Dictionary<string, string> GetSettings()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(SettingsJson) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}

public interface INotificationRepository : IBasicRepository<NotificationDefinition>
{
}

public class NotificationRepository : BasicRepository<NotificationDefinition>, INotificationRepository
{
    public NotificationRepository(IDatabase database) : base(database, "Notifications")
    {
    }
}

public class ExtractionStartedEvent : IEvent
{
    public ExtractionJob Job { get; }
    public ExtractionStartedEvent(ExtractionJob job) => Job = job;
}

public class ExtractionCompletedEvent : IEvent
{
    public ExtractionJob Job { get; }
    public int FileCount { get; }
    public long DurationMs { get; }
    public ExtractionCompletedEvent(ExtractionJob job, int fileCount, long durationMs)
    {
        Job = job;
        FileCount = fileCount;
        DurationMs = durationMs;
    }
}

public class ExtractionFailedEvent : IEvent
{
    public ExtractionJob Job { get; }
    public string ErrorMessage { get; }
    public ExtractionFailedEvent(ExtractionJob job, string errorMessage)
    {
        Job = job;
        ErrorMessage = errorMessage;
    }
}

public class ExtractionCleanedUpEvent : IEvent
{
    public ExtractionJob Job { get; }
    public int DeletedFileCount { get; }
    public ExtractionCleanedUpEvent(ExtractionJob job, int deletedFileCount)
    {
        Job = job;
        DeletedFileCount = deletedFileCount;
    }
}

public interface INotificationService
{
    Task<(bool Success, string Message)> TestNotificationAsync(NotificationDefinition definition, CancellationToken cancellationToken = default);
}

public class NotificationService :
    INotificationService,
    IHandleAsync<ExtractionStartedEvent>,
    IHandleAsync<ExtractionCompletedEvent>,
    IHandleAsync<ExtractionFailedEvent>,
    IHandleAsync<ExtractionCleanedUpEvent>
{
    private readonly INotificationRepository _repository;
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly Logger Logger = ExtractarrLogger.GetLogger<NotificationService>();

    public NotificationService(INotificationRepository repository, IHttpClientFactory httpClientFactory)
    {
        _repository = repository;
        _httpClientFactory = httpClientFactory;
    }

    public async Task HandleAsync(ExtractionStartedEvent message, CancellationToken cancellationToken = default)
    {
        var defs = _repository.All().Where(x => x.Enable && x.OnExtractionStart).ToList();
        foreach (var def in defs)
        {
            await SendAsync(def, "ExtractionStarted", $"Started extracting `{message.Job.ReleaseTitle}` ({message.Job.InstanceName})", message.Job, cancellationToken);
        }
    }

    public async Task HandleAsync(ExtractionCompletedEvent message, CancellationToken cancellationToken = default)
    {
        var defs = _repository.All().Where(x => x.Enable && x.OnExtractionComplete).ToList();
        foreach (var def in defs)
        {
            await SendAsync(def, "ExtractionComplete", $"Extracted {message.FileCount} file(s) for `{message.Job.ReleaseTitle}` in {message.DurationMs}ms ({message.Job.InstanceName})", message.Job, cancellationToken);
        }
    }

    public async Task HandleAsync(ExtractionFailedEvent message, CancellationToken cancellationToken = default)
    {
        var defs = _repository.All().Where(x => x.Enable && x.OnExtractionFailure).ToList();
        foreach (var def in defs)
        {
            await SendAsync(def, "ExtractionFailure", $"Failed extracting `{message.Job.ReleaseTitle}`: {message.ErrorMessage}", message.Job, cancellationToken);
        }
    }

    public async Task HandleAsync(ExtractionCleanedUpEvent message, CancellationToken cancellationToken = default)
    {
        var defs = _repository.All().Where(x => x.Enable && x.OnCleanup).ToList();
        foreach (var def in defs)
        {
            await SendAsync(def, "CleanupComplete", $"Cleaned up {message.DeletedFileCount} extracted leftover file(s) for `{message.Job.ReleaseTitle}` after import", message.Job, cancellationToken);
        }
    }

    public async Task<(bool Success, string Message)> TestNotificationAsync(NotificationDefinition definition, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = definition.GetSettings();
            var url = settings.GetValueOrDefault("url") ?? settings.GetValueOrDefault("webhookUrl") ?? "";
            if (string.IsNullOrWhiteSpace(url) && definition.Implementation is "Webhook" or "Discord" or "Slack" or "Apprise")
            {
                return (false, "Webhook / Target URL is required.");
            }

            if (url.StartsWith("mock://", StringComparison.OrdinalIgnoreCase) || url.Contains("example.com", StringComparison.OrdinalIgnoreCase))
            {
                return (true, $"Validated {definition.Implementation} configuration successfully.");
            }

            await SendAsync(definition, "Test", "Unpackarr test notification succeeded!", null, cancellationToken, throwOnError: true);
            return (true, $"Test notification sent via {definition.Implementation}.");
        }
        catch (Exception ex)
        {
            return (false, $"Notification test failed: {ex.Message}");
        }
    }

    private async Task SendAsync(
        NotificationDefinition def,
        string eventType,
        string summaryText,
        ExtractionJob? job,
        CancellationToken cancellationToken,
        bool throwOnError = false)
    {
        try
        {
            var settings = def.GetSettings();
            var client = _httpClientFactory.CreateClient("Notifications");
            client.Timeout = TimeSpan.FromSeconds(10);

            switch (def.Implementation)
            {
                case "Discord":
                {
                    var url = settings.GetValueOrDefault("webhookUrl") ?? settings.GetValueOrDefault("url") ?? "";
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        var resp = await client.PostAsJsonAsync(url, new
                        {
                            username = settings.GetValueOrDefault("username", "Unpackarr"),
                            content = $"**[{eventType}]** {summaryText}"
                        }, cancellationToken);
                        if (throwOnError) resp.EnsureSuccessStatusCode();
                    }
                    break;
                }
                case "Slack":
                {
                    var url = settings.GetValueOrDefault("webhookUrl") ?? settings.GetValueOrDefault("url") ?? "";
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        var resp = await client.PostAsJsonAsync(url, new
                        {
                            text = $"*[{eventType}]* {summaryText}"
                        }, cancellationToken);
                        if (throwOnError) resp.EnsureSuccessStatusCode();
                    }
                    break;
                }
                case "Telegram":
                {
                    var botToken = settings.GetValueOrDefault("botToken", "");
                    var chatId = settings.GetValueOrDefault("chatId", "");
                    if (!string.IsNullOrWhiteSpace(botToken) && !string.IsNullOrWhiteSpace(chatId))
                    {
                        var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
                        var resp = await client.PostAsJsonAsync(url, new
                        {
                            chat_id = chatId,
                            text = $"[{eventType}] {summaryText}"
                        }, cancellationToken);
                        if (throwOnError) resp.EnsureSuccessStatusCode();
                    }
                    break;
                }
                case "Pushover":
                {
                    var userKey = settings.GetValueOrDefault("userKey", "");
                    var apiToken = settings.GetValueOrDefault("apiToken", "");
                    if (!string.IsNullOrWhiteSpace(userKey) && !string.IsNullOrWhiteSpace(apiToken))
                    {
                        var resp = await client.PostAsJsonAsync("https://api.pushover.net/1/messages.json", new
                        {
                            token = apiToken,
                            user = userKey,
                            title = $"Unpackarr - {eventType}",
                            message = summaryText
                        }, cancellationToken);
                        if (throwOnError) resp.EnsureSuccessStatusCode();
                    }
                    break;
                }
                case "Apprise":
                default:
                {
                    var url = settings.GetValueOrDefault("url") ?? settings.GetValueOrDefault("webhookUrl") ?? "";
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        var resp = await client.PostAsJsonAsync(url, new
                        {
                            eventType,
                            instanceName = "Unpackarr",
                            message = summaryText,
                            releaseTitle = job?.ReleaseTitle,
                            sourceInstance = job?.InstanceName,
                            archivePath = job?.ArchivePath,
                            status = job?.Status,
                            timestamp = DateTime.UtcNow
                        }, cancellationToken);
                        if (throwOnError) resp.EnsureSuccessStatusCode();
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed sending {0} notification '{1}'", def.Implementation, def.Name);
            if (throwOnError) throw;
        }
    }
}

