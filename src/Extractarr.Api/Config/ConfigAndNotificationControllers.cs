using Microsoft.AspNetCore.Mvc;
using Extractarr.Core.Configuration;
using Extractarr.Core.Notifications;

namespace Extractarr.Api.Config;

[ApiController]
[Route("api/v3/config")]
public class ConfigController : ControllerBase
{
    private readonly IConfigService _configService;

    public ConfigController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet("extraction")]
    public ActionResult<ExtractionConfigDto> GetExtractionConfig()
    {
        return Ok(_configService.GetExtractionConfig());
    }

    [HttpPut("extraction")]
    [HttpPost("extraction")]
    public ActionResult<ExtractionConfigDto> SaveExtractionConfig([FromBody] ExtractionConfigDto dto)
    {
        return Ok(_configService.SaveExtractionConfig(dto));
    }

    [HttpGet("host")]
    public ActionResult<HostConfigDto> GetHostConfig()
    {
        return Ok(_configService.GetHostConfig());
    }

    [HttpPut("host")]
    [HttpPost("host")]
    public ActionResult<HostConfigDto> SaveHostConfig([FromBody] HostConfigDto dto)
    {
        return Ok(_configService.SaveHostConfig(dto));
    }

    [HttpPost("host/apikey")]
    public ActionResult ResetApiKey()
    {
        var newKey = _configService.ResetApiKey();
        return Ok(new { apiKey = newKey });
    }

    [HttpGet("ui")]
    public ActionResult<UiConfigDto> GetUiConfig()
    {
        return Ok(_configService.GetUiConfig());
    }

    [HttpPut("ui")]
    [HttpPost("ui")]
    public ActionResult<UiConfigDto> SaveUiConfig([FromBody] UiConfigDto dto)
    {
        return Ok(_configService.SaveUiConfig(dto));
    }
}

[ApiController]
[Route("api/v3/notification")]
public class NotificationController : ControllerBase
{
    private readonly INotificationRepository _repository;
    private readonly INotificationService _notificationService;

    public NotificationController(INotificationRepository repository, INotificationService notificationService)
    {
        _repository = repository;
        _notificationService = notificationService;
    }

    [HttpGet]
    public ActionResult<List<NotificationDefinition>> GetAll()
    {
        return Ok(_repository.All().ToList());
    }

    [HttpGet("schema")]
    public ActionResult GetSchemas()
    {
        var schemas = new object[]
        {
            new { implementation = "Webhook", name = "Webhook", fields = new[] { "url", "method" } },
            new { implementation = "Discord", name = "Discord", fields = new[] { "webhookUrl", "username" } },
            new { implementation = "Slack", name = "Slack", fields = new[] { "webhookUrl", "channel" } },
            new { implementation = "Telegram", name = "Telegram", fields = new[] { "botToken", "chatId" } },
            new { implementation = "Apprise", name = "Apprise", fields = new[] { "url", "notificationUrls" } },
            new { implementation = "Pushover", name = "Pushover", fields = new[] { "userKey", "apiToken" } }
        };
        return Ok(schemas);
    }

    [HttpPost]
    public ActionResult<NotificationDefinition> Create([FromBody] NotificationDefinition model)
    {
        model.Id = 0;
        var created = _repository.Insert(model);
        return Ok(created);
    }

    [HttpPut("{id:int}")]
    public ActionResult<NotificationDefinition> Update(int id, [FromBody] NotificationDefinition model)
    {
        var existing = _repository.Find(id);
        if (existing == null) return NotFound();
        model.Id = id;
        return Ok(_repository.Update(model));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        _repository.Delete(id);
        return NoContent();
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] NotificationDefinition model, CancellationToken cancellationToken)
    {
        var (success, message) = await _notificationService.TestNotificationAsync(model, cancellationToken);
        return Ok(new { isValid = success, message });
    }
}

