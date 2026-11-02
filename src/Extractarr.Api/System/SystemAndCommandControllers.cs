using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Extractarr.Common.Disk;
using Extractarr.Common.EnvironmentInfo;
using Extractarr.Common.Instrumentation;
using Extractarr.Core.Configuration;
using Extractarr.Core.HealthCheck;
using Extractarr.Core.Jobs;
using Extractarr.Core.Messaging.Commands;

namespace Extractarr.Api.System;

[ApiController]
[Route("api/v3")]
public class SystemController : ControllerBase
{
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IHealthCheckService _healthCheckService;
    private readonly IDiskProvider _diskProvider;
    private readonly ITaskManager _taskManager;
    private readonly IConfigService _configService;

    public SystemController(
        IAppFolderInfo appFolderInfo,
        IHealthCheckService healthCheckService,
        IDiskProvider diskProvider,
        ITaskManager taskManager,
        IConfigService configService)
    {
        _appFolderInfo = appFolderInfo;
        _healthCheckService = healthCheckService;
        _diskProvider = diskProvider;
        _taskManager = taskManager;
        _configService = configService;
    }

    [HttpGet("system/status")]
    public ActionResult GetSystemStatus()
    {
        var hostCfg = _configService.GetHostConfig();
        return Ok(new
        {
            appName = "Extractarr",
            instanceName = hostCfg.InstanceName,
            version = "1.0.0.0",
            buildTime = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            isDebug = false,
            isProduction = true,
            isAdmin = true,
            isUserInteractive = Environment.UserInteractive,
            startupPath = AppContext.BaseDirectory,
            appData = _appFolderInfo.AppDataFolder,
            osName = OsInfo.OsName,
            osPlatform = OsInfo.IsWindows ? "windows" : (OsInfo.IsOsx ? "osx" : "linux"),
            architecture = OsInfo.Architecture,
            directorySeparator = Path.DirectorySeparatorChar.ToString(),
            defaultDownloadsPath = OsPathTranslator.GetDefaultDownloadsDirectory(),
            isLinux = OsInfo.IsLinux,
            isOsx = OsInfo.IsOsx,
            isWindows = OsInfo.IsWindows,
            isDocker = OsInfo.IsDocker,
            mode = OsInfo.RuntimeMode,
            branch = "main",
            databaseType = "sqLite",
            databaseVersion = "3.46.0",
            authentication = hostCfg.AuthenticationMethod,
            migrationVersion = 1,
            urlBase = hostCfg.UrlBase,
            runtimeVersion = OsInfo.RuntimeVersion,
            runtimeName = ".NET",
            startTime = OsInfo.StartTime,
            packageVersion = "1.0.0.0",
            packageAuthor = "Extractarr",
            packageUpdateMechanism = OsInfo.IsDocker ? "docker" : "external"
        });
    }

    [HttpPost("system/translate-path")]
    public ActionResult TranslatePath([FromBody] JsonElement body)
    {
        var rawPath = body.TryGetProperty("path", out var p) ? p.GetString() : string.Empty;
        var translated = OsPathTranslator.TranslateToCurrentOs(rawPath);
        return Ok(new
        {
            originalPath = rawPath,
            translatedPath = translated,
            isWindows = OsInfo.IsWindows,
            exists = !string.IsNullOrWhiteSpace(translated) && (global::System.IO.File.Exists(translated) || Directory.Exists(translated))
        });
    }

    [HttpGet("health")]
    public ActionResult<List<HealthCheckResult>> GetHealth()
    {
        return Ok(_healthCheckService.PerformHealthChecks());
    }

    [HttpGet("diskspace")]
    public ActionResult<List<DiskSpaceInfo>> GetDiskSpace()
    {
        return Ok(_diskProvider.GetDiskSpace());
    }

    [HttpGet("system/task")]
    public ActionResult<List<ScheduledTaskDto>> GetScheduledTasks()
    {
        return Ok(_taskManager.GetAllTasks());
    }

    [HttpGet("log")]
    public ActionResult GetLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? level = null,
        [FromQuery] string? search = null)
    {
        var all = MemoryRingTarget.GetEntries().AsEnumerable();
        if (!string.IsNullOrWhiteSpace(level) && !level.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            all = all.Where(x => x.Level.Equals(level.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            all = all.Where(x =>
                x.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Logger.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var list = all.ToList();
        var records = list.Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).ToList();

        return Ok(new
        {
            page,
            pageSize,
            totalRecords = list.Count,
            records
        });
    }

    [HttpGet("log/file")]
    public ActionResult GetLogFiles()
    {
        if (!Directory.Exists(_appFolderInfo.LogFolder))
        {
            return Ok(Array.Empty<object>());
        }

        var files = new DirectoryInfo(_appFolderInfo.LogFolder)
            .GetFiles("*.txt")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select((f, idx) => new
            {
                id = idx + 1,
                filename = f.Name,
                lastWriteTime = f.LastWriteTimeUtc,
                size = f.Length,
                downloadUrl = $"/api/v3/log/file/{Uri.EscapeDataString(f.Name)}"
            })
            .ToList();

        return Ok(files);
    }

    [HttpGet("log/file/{filename}")]
    public IActionResult DownloadLogFile(string filename)
    {
        var safeName = Path.GetFileName(filename);
        var fullPath = Path.Combine(_appFolderInfo.LogFolder, safeName);
        if (!global::System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return File(stream, "text/plain", safeName);
    }
}

[ApiController]
[Route("api/v3/command")]
public class CommandController : ControllerBase
{
    private readonly IManageCommandQueue _commandQueue;
    private readonly ITaskManager _taskManager;

    public CommandController(IManageCommandQueue commandQueue, ITaskManager taskManager)
    {
        _commandQueue = commandQueue;
        _taskManager = taskManager;
    }

    [HttpGet]
    public ActionResult GetAllCommands()
    {
        var all = _commandQueue.All().Select(CommandQueueManager.ToDto).ToList();
        return Ok(all);
    }

    [HttpGet("{id:int}")]
    public ActionResult GetCommandById(int id)
    {
        var cmd = _commandQueue.Get(id);
        if (cmd == null) return NotFound();
        return Ok(CommandQueueManager.ToDto(cmd));
    }

    [HttpPost]
    public ActionResult QueueCommand([FromBody] JsonElement body)
    {
        var name = body.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Command 'name' is required." });
        }

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in body.EnumerateObject())
        {
            if (!prop.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                dict[prop.Name] = prop.Value;
            }
        }

        var cmd = _commandQueue.PushByName(name!, dict, CommandTrigger.Manual);
        if (cmd == null)
        {
            return BadRequest(new { message = $"Unknown command '{name}'." });
        }

        _taskManager.RecordExecution(name!);
        return Ok(CommandQueueManager.ToDto(cmd));
    }
}
