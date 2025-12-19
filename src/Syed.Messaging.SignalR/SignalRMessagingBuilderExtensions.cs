using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Syed.Messaging.SignalR;

/// <summary>
/// Extension methods for adding SignalR bridges to the messaging pipeline.
/// </summary>
public static class SignalRMessagingBuilderExtensions
{
    /// <summary>
    /// Adds a SignalR bridge that forwards events of type <typeparamref name="TEvent"/> 
    /// to a SignalR hub of type <typeparamref name="THub"/>.
    /// </summary>
    public static MessagingBuilder AddSignalRBridge<TEvent, THub, TBridge>(
        this MessagingBuilder builder,
        Action<ConsumerOptions<TEvent>> configure)
        where THub : Hub
        where TBridge : class, ISignalRBridge<TEvent>
    {
        builder.Services.AddSingleton<ISignalRBridge<TEvent>, TBridge>();
        
        builder.AddConsumer<TEvent, SignalRBridgeConsumer<TEvent, THub>>(configure);

        return builder;
    }

    /// <summary>
    /// Adds a SignalR bridge with an inline bridge configuration.
    /// </summary>
    public static MessagingBuilder AddSignalRBridge<TEvent, THub>(
        this MessagingBuilder builder,
        string methodName,
        Func<TEvent, string> groupSelector,
        Func<TEvent, object>? payloadSelector = null,
        Action<ConsumerOptions<TEvent>>? configure = null)
        where THub : Hub
    {
        builder.Services.AddSingleton<ISignalRBridge<TEvent>>(
            new InlineSignalRBridge<TEvent>(methodName, groupSelector, payloadSelector ?? (e => e!)));

        var options = new ConsumerOptions<TEvent>();
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        builder.Services.AddScoped<IMessageHandler<TEvent>, SignalRBridgeConsumer<TEvent, THub>>();
        builder.Services.AddHostedService<GenericMessageConsumer<TEvent>>();

        return builder;
    }
}

/// <summary>
/// Inline implementation of ISignalRBridge for simple cases.
/// </summary>
internal sealed class InlineSignalRBridge<TEvent> : ISignalRBridge<TEvent>
{
    private readonly Func<TEvent, string> _groupSelector;
    private readonly Func<TEvent, object> _payloadSelector;

    public InlineSignalRBridge(
        string methodName,
        Func<TEvent, string> groupSelector,
        Func<TEvent, object> payloadSelector)
    {
        MethodName = methodName;
        _groupSelector = groupSelector;
        _payloadSelector = payloadSelector;
    }

    public string MethodName { get; }

    public string GetGroupName(TEvent evt) => _groupSelector(evt);

    public object GetPayload(TEvent evt) => _payloadSelector(evt);
}
