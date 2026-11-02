using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi.Models;
using NLog;
using Extractarr.Api.System;
using Extractarr.Common.Disk;
using Extractarr.Common.EnvironmentInfo;
using Extractarr.Common.Instrumentation;
using Extractarr.Core.Configuration;
using Extractarr.Core.Datastore;
using Extractarr.Core.Datastore.Migration;
using Extractarr.Core.Extraction;
using Extractarr.Core.HealthCheck;
using Extractarr.Core.Instances;
using Extractarr.Core.Jobs;
using Extractarr.Core.Messaging.Commands;
using Extractarr.Core.Messaging.Events;
using Extractarr.Core.Notifications;
using Extractarr.Core.RemotePathMappings;
using Extractarr.Core.WatchFolders;
using Extractarr.SignalR;

namespace Extractarr.Host;

public static class UnpackarrBootstrap
{
    public static void RegisterUnpackarrServices(IServiceCollection services, IAppFolderInfo? customFolderInfo = null, bool registerHostedWorkers = true)
    {
        var appFolderInfo = customFolderInfo ?? new AppFolderInfo();
        ExtractarrLogger.Configure(appFolderInfo, "Info");

        services.AddSingleton<IAppFolderInfo>(appFolderInfo);
        services.AddSingleton<IDiskProvider, DiskProvider>();
        services.AddSingleton<IDatabase, SqliteDatabase>();
        services.AddSingleton<IMigrationController, MigrationController>();

        // Repositories
        services.AddSingleton<IConfigRepository, ConfigRepository>();
        services.AddSingleton<IScheduledTaskRepository, ScheduledTaskRepository>();
        services.AddSingleton<IArrInstanceRepository, ArrInstanceRepository>();
        services.AddSingleton<IRemotePathMappingRepository, RemotePathMappingRepository>();
        services.AddSingleton<IWatchFolderRepository, WatchFolderRepository>();
        services.AddSingleton<IExtractionJobRepository, ExtractionJobRepository>();
        services.AddSingleton<IExtractionHistoryRepository, ExtractionHistoryRepository>();
        services.AddSingleton<IBlocklistRepository, BlocklistRepository>();
        services.AddSingleton<INotificationRepository, NotificationRepository>();

        // Services & Engines
        services.AddLogging();
        services.AddSignalR();
        services.AddHttpClient();
        services.AddSingleton<IBroadcastSignalRMessage, SignalRBroadcaster>();
        services.AddSingleton<IEventAggregator, EventAggregator>();
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IRemotePathMappingService, RemotePathMappingService>();
        services.AddSingleton<IArrApiClient, ArrApiClient>();
        services.AddSingleton<ISharpCompressExtractor, SharpCompressExtractor>();
        services.AddSingleton<IExtractionLifecycleCoordinator, ExtractionLifecycleCoordinator>();
        services.AddSingleton<IHealthCheckService, HealthCheckService>();

        // Notification Service + Event Handlers
        services.AddSingleton<NotificationService>();
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
        services.AddSingleton<IHandleAsync<ExtractionStartedEvent>>(sp => sp.GetRequiredService<NotificationService>());
        services.AddSingleton<IHandleAsync<ExtractionCompletedEvent>>(sp => sp.GetRequiredService<NotificationService>());
        services.AddSingleton<IHandleAsync<ExtractionFailedEvent>>(sp => sp.GetRequiredService<NotificationService>());
        services.AddSingleton<IHandleAsync<ExtractionCleanedUpEvent>>(sp => sp.GetRequiredService<NotificationService>());

        // Command Queue & Scheduled Tasks
        services.AddSingleton<CommandQueueManager>();
        services.AddSingleton<IManageCommandQueue>(sp => sp.GetRequiredService<CommandQueueManager>());

        services.AddSingleton<WatchFolderService>();
        services.AddSingleton<IWatchFolderService>(sp => sp.GetRequiredService<WatchFolderService>());

        services.AddSingleton<PollArrInstancesTask>();
        services.AddSingleton<IScheduledTask>(sp => sp.GetRequiredService<PollArrInstancesTask>());
        services.AddSingleton<IExecuteAsync<PollArrInstancesCommand>>(sp => sp.GetRequiredService<PollArrInstancesTask>());

        services.AddSingleton<ProcessExtractionQueueTask>();
        services.AddSingleton<IScheduledTask>(sp => sp.GetRequiredService<ProcessExtractionQueueTask>());
        services.AddSingleton<IExecuteAsync<ProcessExtractionQueueCommand>>(sp => sp.GetRequiredService<ProcessExtractionQueueTask>());

        services.AddSingleton<CleanupExtractedFilesTask>();
        services.AddSingleton<IScheduledTask>(sp => sp.GetRequiredService<CleanupExtractedFilesTask>());
        services.AddSingleton<IExecuteAsync<CleanupExtractedFilesCommand>>(sp => sp.GetRequiredService<CleanupExtractedFilesTask>());

        services.AddSingleton<ScanWatchFoldersTask>();
        services.AddSingleton<IScheduledTask>(sp => sp.GetRequiredService<ScanWatchFoldersTask>());
        services.AddSingleton<IExecuteAsync<ScanWatchFoldersCommand>>(sp => sp.GetRequiredService<ScanWatchFoldersTask>());

        services.AddSingleton<CheckHealthTask>();
        services.AddSingleton<IScheduledTask>(sp => sp.GetRequiredService<CheckHealthTask>());
        services.AddSingleton<IExecute<CheckHealthCommand>>(sp => (HealthCheckService)sp.GetRequiredService<IHealthCheckService>());

        services.AddSingleton<HousekeepingTask>();
        services.AddSingleton<IScheduledTask>(sp => sp.GetRequiredService<HousekeepingTask>());
        services.AddSingleton<IExecute<HousekeepingCommand>>(sp => sp.GetRequiredService<HousekeepingTask>());

        services.AddSingleton<TaskManager>();
        services.AddSingleton<ITaskManager>(sp => sp.GetRequiredService<TaskManager>());

        if (registerHostedWorkers)
        {
            services.AddHostedService(sp => sp.GetRequiredService<CommandQueueManager>());
            services.AddHostedService(sp => sp.GetRequiredService<TaskManager>());
            services.AddHostedService(sp => sp.GetRequiredService<WatchFolderService>());
        }
    }

    public static void InitializeDatabase(IServiceProvider serviceProvider)
    {
        var migrator = serviceProvider.GetRequiredService<IMigrationController>();
        migrator.Migrate();

        var taskManager = serviceProvider.GetRequiredService<ITaskManager>();
        taskManager.InitializeTasks();

        // Ensure initial API key & log level are applied
        var configService = serviceProvider.GetRequiredService<IConfigService>();
        var hostConfig = configService.GetHostConfig();
        var appFolder = serviceProvider.GetRequiredService<IAppFolderInfo>();
        ExtractarrLogger.SetLogLevel(hostConfig.LogLevel, appFolder);
    }
}

public class Program
{
    private static string? ResolveStaticFrontendDirectory()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "frontend", "dist"),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "frontend", "dist")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "frontend", "dist")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "frontend", "dist"))
        };

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            candidates.Add(Path.Combine(dir.FullName, "frontend", "dist"));
            candidates.Add(Path.Combine(dir.FullName, "wwwroot"));
            dir = dir.Parent;
        }

        return candidates.FirstOrDefault(d => Directory.Exists(d) && File.Exists(Path.Combine(d, "index.html")));
    }

    public static void Main(string[] args)
    {
        var (customDataDir, customPort) = ParseCommandLineArgs(args);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        // Native Windows Service & Linux systemd daemon integration
        builder.Host.UseWindowsService(options =>
        {
            options.ServiceName = "Extractarr";
        });
        builder.Host.UseSystemd();

        var portEnv = customPort
                      ?? Environment.GetEnvironmentVariable("EXTRACTARR_PORT")
                      ?? Environment.GetEnvironmentVariable("UNPACKARR_PORT")
                      ?? "8686";
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
        {
            builder.WebHost.UseUrls($"http://0.0.0.0:{portEnv}");
        }

        var appFolderInfo = new AppFolderInfo(customDataDir);
        UnpackarrBootstrap.RegisterUnpackarrServices(builder.Services, appFolderInfo);

        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(SystemController).Assembly)
            .AddJsonOptions(opts =>
            {
                opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                opts.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });

        builder.Services.AddSignalR()
            .AddJsonProtocol(opts =>
            {
                opts.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.SetIsOriginAllowed(_ => true)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v3", new OpenApiInfo
            {
                Title = "Extractarr API",
                Version = "v3",
                Description = "Sonarr v5-styled Automated Archive Extraction Engine & GUI for the *arr Ecosystem"
            });
            c.AddSecurityDefinition("X-Api-Key", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Name = "X-Api-Key",
                Type = SecuritySchemeType.ApiKey,
                Description = "Extractarr API Key"
            });
        });

        var app = builder.Build();

        UnpackarrBootstrap.InitializeDatabase(app.Services);

        var logger = ExtractarrLogger.GetLogger<Program>();
        logger.Info("Extractarr v1.0.0.0 ({0} / {1}) starting up on port {2}...", OsInfo.OsName, OsInfo.RuntimeMode, portEnv);

        app.UseCors();

        app.UseSwagger(c =>
        {
            c.RouteTemplate = "swagger/{documentName}/swagger.json";
        });
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v3/swagger.json", "Extractarr API v3");
            c.RoutePrefix = "swagger";
        });

        var activeStaticDir = ResolveStaticFrontendDirectory();
        if (activeStaticDir != null)
        {
            logger.Info("Serving Sonarr v5 React UI from: {0}", activeStaticDir);
            var fileProvider = new PhysicalFileProvider(activeStaticDir);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
        }

        app.MapControllers();
        app.MapHub<MessageHub>("/signalr/messages");

        if (activeStaticDir != null)
        {
            var indexHtml = Path.Combine(activeStaticDir, "index.html");
            app.MapFallback(async context =>
            {
                if (context.Request.Path.StartsWithSegments("/api") ||
                    context.Request.Path.StartsWithSegments("/signalr") ||
                    context.Request.Path.StartsWithSegments("/swagger"))
                {
                    context.Response.StatusCode = 404;
                    return;
                }

                if (File.Exists(indexHtml))
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.SendFileAsync(indexHtml);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
            });
        }

        app.Run();
    }

    private static (string? DataDir, string? Port) ParseCommandLineArgs(string[] args)
    {
        string? dataDir = null;
        string? port = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--data=", StringComparison.OrdinalIgnoreCase))
            {
                dataDir = arg["--data=".Length..].Trim('"');
            }
            else if ((arg.Equals("--data", StringComparison.OrdinalIgnoreCase) || arg.Equals("-d", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                dataDir = args[++i].Trim('"');
            }
            else if (arg.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
            {
                port = arg["--port=".Length..].Trim('"');
            }
            else if ((arg.Equals("--port", StringComparison.OrdinalIgnoreCase) || arg.Equals("-p", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                port = args[++i].Trim('"');
            }
        }

        return (dataDir, port);
    }
}

