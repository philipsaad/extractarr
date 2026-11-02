using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using Extractarr.Common.Instrumentation;
using Extractarr.Core.Datastore;
using Extractarr.SignalR;

namespace Extractarr.Core.Messaging.Commands;

public enum CommandTrigger
{
    Unspecified = 0,
    Manual = 1,
    Scheduled = 2
}

public enum CommandPriority
{
    Low = -1,
    Normal = 0,
    High = 1
}

public enum CommandStatus
{
    Queued = 0,
    Started = 1,
    Completed = 2,
    Failed = 3,
    Aborted = 4
}

public abstract class Command
{
    public virtual bool SendUpdatesToClient => true;
    public virtual bool UpdateScheduledTask => true;
    public virtual string CompletionMessage => "Completed";
    public virtual bool RequiresDiskAccess => false;
    public string Name => GetType().Name.Replace("Command", "");
    public CommandTrigger Trigger { get; set; } = CommandTrigger.Unspecified;
    public bool SuppressMessages { get; set; }
}

public interface IExecute<in TCommand> where TCommand : Command
{
    void Execute(TCommand message);
}

public interface IExecuteAsync<in TCommand> where TCommand : Command
{
    Task ExecuteAsync(TCommand message, CancellationToken cancellationToken = default);
}

public class CommandModel : ModelBase
{
    public string Name { get; set; } = string.Empty;
    public string CommandName { get; set; } = string.Empty;
    public string? Message { get; set; }
    public Command bodyObject { get; set; } = null!;
    public string? Body { get; set; }
    public CommandPriority Priority { get; set; } = CommandPriority.Normal;
    public CommandStatus Status { get; set; } = CommandStatus.Queued;
    public DateTime Queued { get; set; } = DateTime.UtcNow;
    public DateTime? Started { get; set; }
    public DateTime? Ended { get; set; }
    public TimeSpan? Duration => Started.HasValue && Ended.HasValue ? Ended.Value - Started.Value : null;
    public string? Exception { get; set; }
    public CommandTrigger Trigger { get; set; } = CommandTrigger.Unspecified;
    public DateTime StateChangeTime { get; set; } = DateTime.UtcNow;
    public bool SendUpdatesToClient { get; set; } = true;
    public bool UpdateScheduledTask { get; set; } = true;
}

public interface IManageCommandQueue
{
    CommandModel Push<TCommand>(TCommand command, CommandPriority priority = CommandPriority.Normal, CommandTrigger trigger = CommandTrigger.Unspecified)
        where TCommand : Command;
    CommandModel? PushByName(string commandName, Dictionary<string, object?>? parameters = null, CommandTrigger trigger = CommandTrigger.Manual);
    List<CommandModel> All();
    CommandModel? Get(int id);
}

public class CommandQueueManager : BackgroundService, IManageCommandQueue
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IBroadcastSignalRMessage _signalRBroadcaster;
    private readonly Channel<CommandModel> _queue = Channel.CreateUnbounded<CommandModel>();
    private readonly ConcurrentDictionary<int, CommandModel> _commands = new();
    private int _nextId;
    private static readonly Logger Logger = ExtractarrLogger.GetLogger<CommandQueueManager>();

    public CommandQueueManager(IServiceProvider serviceProvider, IBroadcastSignalRMessage signalRBroadcaster)
    {
        _serviceProvider = serviceProvider;
        _signalRBroadcaster = signalRBroadcaster;
    }

    public List<CommandModel> All()
    {
        return _commands.Values
            .OrderByDescending(c => c.Queued)
            .Take(100)
            .ToList();
    }

    public CommandModel? Get(int id)
    {
        return _commands.TryGetValue(id, out var cmd) ? cmd : null;
    }

    public CommandModel Push<TCommand>(TCommand command, CommandPriority priority = CommandPriority.Normal, CommandTrigger trigger = CommandTrigger.Unspecified)
        where TCommand : Command
    {
        command.Trigger = trigger;

        // Deduplicate already queued command of the same type
        var existing = _commands.Values.FirstOrDefault(c =>
            c.CommandName.Equals(command.Name, StringComparison.OrdinalIgnoreCase) &&
            (c.Status == CommandStatus.Queued || c.Status == CommandStatus.Started));

        if (existing != null && trigger == CommandTrigger.Scheduled)
        {
            return existing;
        }

        var id = Interlocked.Increment(ref _nextId);
        var model = new CommandModel
        {
            Id = id,
            Name = command.Name,
            CommandName = command.Name,
            Message = "Queued",
            bodyObject = command,
            Body = System.Text.Json.JsonSerializer.Serialize(command, command.GetType()),
            Priority = priority,
            Status = CommandStatus.Queued,
            Queued = DateTime.UtcNow,
            Trigger = trigger,
            StateChangeTime = DateTime.UtcNow,
            SendUpdatesToClient = command.SendUpdatesToClient,
            UpdateScheduledTask = command.UpdateScheduledTask
        };

        _commands[id] = model;
        _queue.Writer.TryWrite(model);

        if (model.SendUpdatesToClient)
        {
            _signalRBroadcaster.BroadcastMessage("command", "created", ToDto(model));
        }

        // Trim old completed commands
        if (_commands.Count > 200)
        {
            var oldIds = _commands.Values
                .Where(c => c.Status is CommandStatus.Completed or CommandStatus.Failed or CommandStatus.Aborted)
                .OrderBy(c => c.Queued)
                .Take(50)
                .Select(c => c.Id)
                .ToList();

            foreach (var oldId in oldIds)
            {
                _commands.TryRemove(oldId, out _);
            }
        }

        return model;
    }

    public CommandModel? PushByName(string commandName, Dictionary<string, object?>? parameters = null, CommandTrigger trigger = CommandTrigger.Manual)
    {
        var normalized = commandName.Replace("Command", "", StringComparison.OrdinalIgnoreCase).Trim();
        var commandTypes = typeof(Command).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Command).IsAssignableFrom(t));

        var matchedType = commandTypes.FirstOrDefault(t =>
            t.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            t.Name.Equals(normalized + "Command", StringComparison.OrdinalIgnoreCase));

        if (matchedType == null)
        {
            Logger.Warn("Unknown command requested: {0}", commandName);
            return null;
        }

        var cmdInstance = (Command)Activator.CreateInstance(matchedType)!;
        if (parameters != null)
        {
            foreach (var kv in parameters)
            {
                var prop = matchedType.GetProperty(kv.Key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (prop != null && prop.CanWrite && kv.Value != null)
                {
                    try
                    {
                        var val = kv.Value is System.Text.Json.JsonElement je
                            ? System.Text.Json.JsonSerializer.Deserialize(je.GetRawText(), prop.PropertyType)
                            : Convert.ChangeType(kv.Value, prop.PropertyType);
                        prop.SetValue(cmdInstance, val);
                    }
                    catch
                    {
                        // Ignore conversion mismatch
                    }
                }
            }
        }

        return Push(cmdInstance, CommandPriority.High, trigger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var model in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            model.Status = CommandStatus.Started;
            model.Started = DateTime.UtcNow;
            model.StateChangeTime = DateTime.UtcNow;
            model.Message = $"Executing {model.Name}";

            if (model.SendUpdatesToClient)
            {
                _signalRBroadcaster.BroadcastMessage("command", "updated", ToDto(model));
            }

            try
            {
                var cmd = model.bodyObject;
                var cmdType = cmd.GetType();

                var asyncHandlerType = typeof(IExecuteAsync<>).MakeGenericType(cmdType);
                var asyncHandler = _serviceProvider.GetService(asyncHandlerType);

                if (asyncHandler != null)
                {
                    var method = asyncHandlerType.GetMethod("ExecuteAsync")!;
                    var task = (Task)method.Invoke(asyncHandler, new object[] { cmd, stoppingToken })!;
                    await task;
                }
                else
                {
                    var syncHandlerType = typeof(IExecute<>).MakeGenericType(cmdType);
                    var syncHandler = _serviceProvider.GetService(syncHandlerType);
                    if (syncHandler != null)
                    {
                        var method = syncHandlerType.GetMethod("Execute")!;
                        method.Invoke(syncHandler, new object[] { cmd });
                    }
                    else
                    {
                        Logger.Warn("No command executor registered for {0}", cmdType.Name);
                    }
                }

                model.Status = CommandStatus.Completed;
                model.Ended = DateTime.UtcNow;
                model.StateChangeTime = DateTime.UtcNow;
                model.Message = cmd.CompletionMessage;
            }
            catch (Exception ex)
            {
                var realEx = ex.InnerException ?? ex;
                Logger.Error(realEx, "Command [{0}] failed", model.Name);
                model.Status = CommandStatus.Failed;
                model.Ended = DateTime.UtcNow;
                model.StateChangeTime = DateTime.UtcNow;
                model.Message = realEx.Message;
                model.Exception = realEx.ToString();
            }

            if (model.SendUpdatesToClient)
            {
                _signalRBroadcaster.BroadcastMessage("command", "updated", ToDto(model));
            }
        }
    }

    public static object ToDto(CommandModel m) => new
    {
        id = m.Id,
        name = m.Name,
        commandName = m.CommandName,
        message = m.Message,
        priority = m.Priority.ToString().ToLowerInvariant(),
        status = m.Status.ToString().ToLowerInvariant(),
        queued = m.Queued,
        started = m.Started,
        ended = m.Ended,
        duration = m.Duration?.ToString(@"hh\:mm\:ss\.fff"),
        trigger = m.Trigger.ToString().ToLowerInvariant(),
        stateChangeTime = m.StateChangeTime,
        sendUpdatesToClient = m.SendUpdatesToClient,
        updateScheduledTask = m.UpdateScheduledTask
    };
}

