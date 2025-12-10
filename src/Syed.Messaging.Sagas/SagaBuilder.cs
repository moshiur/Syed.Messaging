using Microsoft.Extensions.DependencyInjection;
using Syed.Messaging;

namespace Syed.Messaging.Sagas;

public sealed class SagaBuilder<TSagaState, TSaga>
    where TSagaState : class, ISagaState, new()
    where TSaga : class
{
    private readonly List<SagaCorrelationConfig> _correlations = new();

    internal IReadOnlyList<SagaCorrelationConfig> Correlations => _correlations;

    /// <summary>
    /// Configure how a given message type maps to a saga instance via a correlation key.
    /// </summary>
    public SagaBuilder<TSagaState, TSaga> CorrelateOn<TMessage>(
        Func<TMessage, object> keySelector,
        bool startsNew = false)
    {
        _correlations.Add(new SagaCorrelationConfig
        {
            MessageType = typeof(TMessage),
            StartsNewSaga = startsNew,
            KeySelector = msg => keySelector((TMessage)msg).ToString() ?? string.Empty
        });

        return this;
    }
}

public sealed class SagaRegistryBuilder
{
    private readonly IServiceCollection _services;
    private readonly List<SagaDefinition> _definitions = new();

    public SagaRegistryBuilder(IServiceCollection services)
    {
        _services = services;
    }

    public SagaRegistryBuilder AddSaga<TSagaState, TSaga>(
        Action<SagaBuilder<TSagaState, TSaga>> configure)
        where TSagaState : class, ISagaState, new()
        where TSaga : class
    {
        // Register saga and state store interfaces
        _services.AddScoped<TSaga>();
        _services.AddSingleton<ISagaStateStore<TSagaState>, InMemorySagaStateStore<TSagaState>>();

        var builder = new SagaBuilder<TSagaState, TSaga>();
        configure(builder);

        var def = new SagaDefinition
        {
            StateType = typeof(TSagaState),
            SagaType = typeof(TSaga),
            Correlations = builder.Correlations.ToList()
        };

        _definitions.Add(def);
        return this;
    }

    internal ISagaRegistry Build() => new SagaRegistry(_definitions);
}
