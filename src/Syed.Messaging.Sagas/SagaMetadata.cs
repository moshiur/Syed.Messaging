using Syed.Messaging;

namespace Syed.Messaging.Sagas;

public sealed class SagaCorrelationConfig
{
    public required Type MessageType { get; init; }
    public required Func<object, string> KeySelector { get; init; }
    public bool StartsNewSaga { get; init; }
}

public sealed class SagaDefinition
{
    public required Type StateType { get; init; }
    public required Type SagaType { get; init; }
    public required IReadOnlyList<SagaCorrelationConfig> Correlations { get; init; }
}

public interface ISagaRegistry
{
    SagaDefinition? FindByMessageType(Type messageType);
}

internal sealed class SagaRegistry : ISagaRegistry
{
    private readonly IReadOnlyList<SagaDefinition> _definitions;

    public SagaRegistry(IReadOnlyList<SagaDefinition> definitions)
    {
        _definitions = definitions;
    }

    public SagaDefinition? FindByMessageType(Type messageType)
    {
        // First match wins
        return _definitions.FirstOrDefault(d =>
            d.Correlations.Any(c => c.MessageType == messageType));
    }
}
