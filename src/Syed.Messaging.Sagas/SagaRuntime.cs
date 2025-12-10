using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Syed.Messaging;

namespace Syed.Messaging.Sagas;

public interface ISagaRuntime
{
    Task HandleAsync(object message, MessageContext context, CancellationToken ct);
}

internal sealed class SagaRuntime : ISagaRuntime
{
    private readonly ISagaRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly ILogger<SagaRuntime> _logger;

    public SagaRuntime(ISagaRegistry registry, IServiceProvider services, ILogger<SagaRuntime> logger)
    {
        _registry = registry;
        _services = services;
        _logger = logger;
    }

    public async Task HandleAsync(object message, MessageContext context, CancellationToken ct)
    {
        var messageType = message.GetType();
        var definition = _registry.FindByMessageType(messageType);
        if (definition is null)
        {
            _logger.LogDebug("No saga registered for message type {MessageType}", messageType.FullName);
            return;
        }

        var correlationConfig = definition.Correlations
            .FirstOrDefault(c => c.MessageType == messageType);

        if (correlationConfig is null)
        {
            _logger.LogDebug("Saga {SagaType} does not correlate on message type {MessageType}", definition.SagaType.Name, messageType.FullName);
            return;
        }

        var correlationKey = correlationConfig.KeySelector(message);
        if (string.IsNullOrWhiteSpace(correlationKey))
        {
            _logger.LogWarning("Correlation key for saga {SagaType} and message {MessageType} is empty.", definition.SagaType.Name, messageType.FullName);
            return;
        }

        // Resolve the state store
        var storeType = typeof(ISagaStateStore<>).MakeGenericType(definition.StateType);
        dynamic store = _services.GetRequiredService(storeType);

        // Load or create state
        dynamic? state = await store.LoadAsync(correlationKey, ct);
        if (state is null)
        {
            if (!correlationConfig.StartsNewSaga)
            {
                _logger.LogInformation("Message {MessageType} with correlation key {Key} does not start saga {SagaType}. Ignoring.",
                    messageType.Name, correlationKey, definition.SagaType.Name);
                return;
            }

            _logger.LogInformation("Creating new saga instance {SagaType} with key {Key}", definition.SagaType.Name, correlationKey);
            state = Activator.CreateInstance(definition.StateType)!;
            state.Id = Guid.NewGuid();
        }

        // Resolve saga instance
        var saga = _services.GetRequiredService(definition.SagaType);

        // Find the correct ISagaHandler interface
        var sagaHandlerInterface = definition.SagaType.GetInterfaces()
            .FirstOrDefault(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(ISagaHandler<,>) &&
                i.GetGenericArguments()[0] == definition.StateType &&
                i.GetGenericArguments()[1] == messageType);

        if (sagaHandlerInterface is null)
        {
            _logger.LogWarning("Saga {SagaType} does not handle message type {MessageType}.", definition.SagaType.Name, messageType.FullName);
            return;
        }

        var method = sagaHandlerInterface.GetMethod("HandleAsync");
        if (method is null)
        {
            _logger.LogError("Saga {SagaType} handler for {MessageType} does not define HandleAsync.", definition.SagaType.Name, messageType.FullName);
            return;
        }

        await (Task)method.Invoke(saga, new object[] { state, message, context, ct })!;

        // Save saga state
        await store.SaveAsync(state, correlationKey, ct);
    }
}

/// <summary>
/// Adapts saga runtime to the IMessageHandler{T} abstraction used by Syed.Messaging.
/// </summary>
public sealed class SagaMessageHandler<TMessage> : IMessageHandler<TMessage>
{
    private readonly ISagaRuntime _runtime;

    public SagaMessageHandler(ISagaRuntime runtime)
    {
        _runtime = runtime;
    }

    public Task HandleAsync(TMessage message, MessageContext context, CancellationToken ct)
        => _runtime.HandleAsync(message!, context, ct);
}
