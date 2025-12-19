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
}
