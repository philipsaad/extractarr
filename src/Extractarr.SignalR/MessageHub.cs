using Microsoft.AspNetCore.SignalR;
using NLog;
using Extractarr.Common.Instrumentation;

namespace Extractarr.SignalR;

public class SignalRMessage
{
    public string Name { get; set; } = string.Empty;
    public object? Body { get; set; }
    public string Action { get; set; } = "updated";
}

public interface IBroadcastSignalRMessage
{
    Task BroadcastMessageAsync(SignalRMessage message, CancellationToken cancellationToken = default);
    void BroadcastMessage(string name, string action, object? body = null);
}

public class MessageHub : Hub
{
    private static readonly Logger Logger = ExtractarrLogger.GetLogger<MessageHub>();

    public override async Task OnConnectedAsync()
    {
        Logger.Debug("SignalR client connected: {0}", Context.ConnectionId);
        await Clients.Caller.SendAsync("receiveMessage", new SignalRMessage
        {
            Name = "version",
            Action = "connected",
            Body = new { version = "1.0.0.0" }
        });
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Logger.Debug("SignalR client disconnected: {0}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

public class SignalRBroadcaster : IBroadcastSignalRMessage
{
    private readonly IHubContext<MessageHub> _hubContext;
    private static readonly Logger Logger = ExtractarrLogger.GetLogger<SignalRBroadcaster>();

    public SignalRBroadcaster(IHubContext<MessageHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task BroadcastMessageAsync(SignalRMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("receiveMessage", message, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to broadcast SignalR message {0}:{1}", message.Name, message.Action);
        }
    }

    public void BroadcastMessage(string name, string action, object? body = null)
    {
        _ = BroadcastMessageAsync(new SignalRMessage
        {
            Name = name,
            Action = action,
            Body = body
        });
    }
}

