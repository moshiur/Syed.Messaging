using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Syed.Messaging;

public sealed class MessagingBuilder
{
    public IServiceCollection Services { get; }

    internal MessagingBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public MessagingBuilder AddConsumer<TMessage, THandler>(
        Action<ConsumerOptions<TMessage>> configure)
        where THandler : class, IMessageHandler<TMessage>
    {
        Services.AddScoped<IMessageHandler<TMessage>, THandler>();

        var options = new ConsumerOptions<TMessage>();
        configure(options);
        Services.AddSingleton(options);

        Services.AddHostedService<GenericMessageConsumer<TMessage>>();

        return this;
    }

    public MessagingBuilder UseSerializer<TSerializer>() where TSerializer : class, ISerializer
    {
        Services.Replace(ServiceDescriptor.Singleton<ISerializer, TSerializer>());
        return this;
    }

    /// <summary>
    /// Registers an RPC handler that responds to requests with a response message.
    /// </summary>
    public MessagingBuilder AddRpcHandler<TRequest, TResponse, THandler>(
        Action<ConsumerOptions<TRequest>> configure)
        where THandler : class, IRpcHandler<TRequest, TResponse>
    {
        Services.AddScoped<IRpcHandler<TRequest, TResponse>, THandler>();

        var options = new ConsumerOptions<TRequest>();
        configure(options);
        Services.AddSingleton(options);

        Services.AddHostedService<RpcMessageConsumer<TRequest, TResponse>>();

        return this;
    }
}

