using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Syed.Messaging.SignalR;

/// <summary>
/// Message handler that bridges messaging events to SignalR hubs.
/// </summary>
/// <typeparam name="TEvent">The event type to consume.</typeparam>
/// <typeparam name="THub">The SignalR hub type to forward to.</typeparam>
public class SignalRBridgeConsumer<TEvent, THub> : IMessageHandler<TEvent>
    where THub : Hub
{
    private readonly IHubContext<THub> _hubContext;
    private readonly ISignalRBridge<TEvent> _bridge;
    private readonly ILogger<SignalRBridgeConsumer<TEvent, THub>> _logger;

    public SignalRBridgeConsumer(
        IHubContext<THub> hubContext,
        ISignalRBridge<TEvent> bridge,
        ILogger<SignalRBridgeConsumer<TEvent, THub>> logger)
    {
        _hubContext = hubContext;
        _bridge = bridge;
        _logger = logger;
    }

    public async Task HandleAsync(TEvent message, MessageContext context, CancellationToken ct)
    {
        var groupName = _bridge.GetGroupName(message);
        var payload = _bridge.GetPayload(message);
        var methodName = _bridge.MethodName;

        _logger.LogDebug(
            "Bridging event {EventType} to SignalR group {GroupName} via method {MethodName}",
            typeof(TEvent).Name,
            groupName,
            methodName);

        await _hubContext.Clients.Group(groupName).SendAsync(methodName, payload, ct);

        _logger.LogInformation(
            "Successfully forwarded {EventType} to SignalR group {GroupName}",
            typeof(TEvent).Name,
            groupName);
    }
}
