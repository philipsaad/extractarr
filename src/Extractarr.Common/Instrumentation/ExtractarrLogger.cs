using System.Collections.Concurrent;
using NLog;
using NLog.Config;
using NLog.Targets;
using Extractarr.Common.EnvironmentInfo;

namespace Extractarr.Common.Instrumentation;

public class InMemoryLogEntry
{
    public int Id { get; set; }
    public DateTime Time { get; set; }
    public string Level { get; set; } = "Info";
    public string Logger { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
}

[Target("UnpackarrMemoryRing")]
public sealed class MemoryRingTarget : TargetWithLayout
{
    private static int _nextId;
    private static readonly ConcurrentQueue<InMemoryLogEntry> _entries = new();
    private const int MaxEntries = 1000;

    public static IReadOnlyList<InMemoryLogEntry> GetEntries() => _entries.OrderByDescending(x => x.Id).ToList();

    public static void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }

    protected override void Write(LogEventInfo logEvent)
    {
        var entry = new InMemoryLogEntry
        {
            Id = Interlocked.Increment(ref _nextId),
            Time = logEvent.TimeStamp.ToUniversalTime(),
            Level = logEvent.Level.Name,
            Logger = logEvent.LoggerName ?? "Unpackarr",
            Message = logEvent.FormattedMessage ?? string.Empty,
            Exception = logEvent.Exception?.ToString()
        };

        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries)
        {
            _entries.TryDequeue(out _);
        }
    }
}

public static class ExtractarrLogger
{
    private static bool _configured;
    private static readonly object _sync = new();

    public static void Configure(IAppFolderInfo appFolderInfo, string minLevelName = "Info")
    {
        lock (_sync)
        {
            var config = new LoggingConfiguration();

            var standardTarget = new FileTarget("standardFile")
            {
                FileName = Path.Combine(appFolderInfo.LogFolder, "unpackarr.txt"),
                ArchiveFileName = Path.Combine(appFolderInfo.LogFolder, "unpackarr.{#}.txt"),
                ArchiveAboveSize = 1024 * 1024 * 2, // 2 MB
                ArchiveNumbering = ArchiveNumberingMode.Rolling,
                MaxArchiveFiles = 5,
                Layout = "${date:format=yyyy-MM-dd HH\\:mm\\:ss.f}|${level}|${logger}|${message}${onexception:inner=${newline}${exception:format=tostring}}"
            };

            var debugTarget = new FileTarget("debugFile")
            {
                FileName = Path.Combine(appFolderInfo.LogFolder, "unpackarr.debug.txt"),
                ArchiveFileName = Path.Combine(appFolderInfo.LogFolder, "unpackarr.debug.{#}.txt"),
                ArchiveAboveSize = 1024 * 1024 * 5, // 5 MB
                ArchiveNumbering = ArchiveNumberingMode.Rolling,
                MaxArchiveFiles = 5,
                Layout = "${date:format=yyyy-MM-dd HH\\:mm\\:ss.fff}|${level}|${logger}|${message}${onexception:inner=${newline}${exception:format=tostring}}"
            };

            var traceTarget = new FileTarget("traceFile")
            {
                FileName = Path.Combine(appFolderInfo.LogFolder, "unpackarr.trace.txt"),
                ArchiveFileName = Path.Combine(appFolderInfo.LogFolder, "unpackarr.trace.{#}.txt"),
                ArchiveAboveSize = 1024 * 1024 * 10, // 10 MB
                ArchiveNumbering = ArchiveNumberingMode.Rolling,
                MaxArchiveFiles = 5,
                Layout = "${date:format=yyyy-MM-dd HH\\:mm\\:ss.ffff}|${level}|${logger}|${message}${onexception:inner=${newline}${exception:format=tostring}}"
            };

            var consoleTarget = new ConsoleTarget("console")
            {
                Layout = "[${level:uppercase=true}] ${logger:shortName=true}: ${message} ${exception:format=shortType,message}"
            };

            var memoryTarget = new MemoryRingTarget
            {
                Name = "memoryRing"
            };

            config.AddTarget(standardTarget);
            config.AddTarget(debugTarget);
            config.AddTarget(traceTarget);
            config.AddTarget(consoleTarget);
            config.AddTarget(memoryTarget);

            var minLevel = ParseLogLevel(minLevelName);

            config.AddRule(LogLevel.Info, LogLevel.Fatal, standardTarget);
            if (minLevel <= LogLevel.Debug)
            {
                config.AddRule(LogLevel.Debug, LogLevel.Fatal, debugTarget);
            }

            if (minLevel <= LogLevel.Trace)
            {
                config.AddRule(LogLevel.Trace, LogLevel.Fatal, traceTarget);
            }

            config.AddRule(minLevel, LogLevel.Fatal, consoleTarget);
            config.AddRule(minLevel, LogLevel.Fatal, memoryTarget);

            LogManager.Configuration = config;
            LogManager.ReconfigExistingLoggers();
            _configured = true;
        }
    }

    public static void SetLogLevel(string levelName, IAppFolderInfo appFolderInfo)
    {
        Configure(appFolderInfo, levelName);
    }

    public static Logger GetLogger(Type type)
    {
        if (!_configured)
        {
            LogManager.Configuration ??= new LoggingConfiguration();
        }

        return LogManager.GetLogger(type.FullName ?? type.Name);
    }

    public static Logger GetLogger<T>() => GetLogger(typeof(T));

    private static LogLevel ParseLogLevel(string? name)
    {
        return (name?.Trim().ToLowerInvariant()) switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "warn" or "warning" => LogLevel.Warn,
            "error" => LogLevel.Error,
            "fatal" => LogLevel.Fatal,
            _ => LogLevel.Info
        };
    }
}

