using Microsoft.Extensions.DependencyInjection;
using NLog;
using Extractarr.Common.Instrumentation;

namespace Extractarr.Core.Messaging.Events;

public interface IEvent
{
}

public interface IHandle<in TEvent> where TEvent : IEvent
{
    void Handle(TEvent message);
}

public interface IHandleAsync<in TEvent> where TEvent : IEvent
{
    Task HandleAsync(TEvent message, CancellationToken cancellationToken = default);
}

public interface IEventAggregator
{
    void PublishEvent<TEvent>(TEvent @event) where TEvent : class, IEvent;
    Task PublishEventAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class, IEvent;
}

public class EventAggregator : IEventAggregator
{
    private readonly IServiceProvider _serviceProvider;
    private static readonly Logger Logger = ExtractarrLogger.GetLogger<EventAggregator>();

    public EventAggregator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void PublishEvent<TEvent>(TEvent @event) where TEvent : class, IEvent
    {
        PublishEventAsync(@event).GetAwaiter().GetResult();
    }

    public async Task PublishEventAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class, IEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        Logger.Trace("Publishing event: {0}", typeof(TEvent).Name);

        var syncHandlers = _serviceProvider.GetServices<IHandle<TEvent>>().ToList();
        foreach (var handler in syncHandlers)
        {
            try
            {
                handler.Handle(@event);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "{0} failed while processing [{1}]", handler.GetType().Name, typeof(TEvent).Name);
            }
        }

        var asyncHandlers = _serviceProvider.GetServices<IHandleAsync<TEvent>>().ToList();
        foreach (var handler in asyncHandlers)
        {
            try
            {
                await handler.HandleAsync(@event, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "{0} failed while processing async [{1}]", handler.GetType().Name, typeof(TEvent).Name);
            }
        }
    }
}

