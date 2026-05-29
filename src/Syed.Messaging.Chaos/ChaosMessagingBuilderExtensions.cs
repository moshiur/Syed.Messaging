using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Syed.Messaging.Chaos;

/// <summary>
/// Opt in to chaos injection on a <see cref="MessagingBuilder"/>.
/// </summary>
public static class ChaosMessagingBuilderExtensions
{
    /// <summary>
    /// Register the chaos middleware. With no arguments, chaos is
    /// <see cref="ChaosLevel.Off"/> until the <c>SYED_CHAOS_LEVEL</c>
    /// environment variable is set. Configure <see cref="ChaosOptions"/> to
    /// set a level in code, mask shapes, seed deterministically, or register
    /// a custom <see cref="IChaosInjector"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddMessaging(m =>
    /// {
    ///     m.UseRabbitMq(o => o.ConnectionString = "amqp://localhost");
    ///     m.AddConsumer&lt;OrderCreated, OrderCreatedHandler&gt;(c => c.Destination = "orders.created");
    ///     m.EnableChaos();   // env-var driven; SYED_CHAOS_LEVEL=medium to activate in dev
    /// });
    /// </code>
    /// </example>
    public static MessagingBuilder EnableChaos(
        this MessagingBuilder builder,
        Action<ChaosOptions>? configure = null)
    {
        var options = new ChaosOptions();
        configure?.Invoke(options);

        // Options is immutable config — singleton.
        builder.Services.AddSingleton(options);

        // Environment resolution + the once-per-process startup log (incl. the
        // production-refusal log) live in a singleton.
        builder.Services.AddSingleton<ChaosEnvironment>();

        // Injector holds RNG state and must be shared + thread-safe — singleton.
        if (options.CustomInjectorType is { } custom)
        {
            builder.Services.TryAddSingleton(typeof(IChaosInjector), custom);
        }
        else
        {
            builder.Services.TryAddSingleton<IChaosInjector>(
                _ => new RealisticChaosInjector(options.Seed));
        }

        // ChaosMiddleware participates in the existing middleware pipeline.
        builder.AddMiddleware<ChaosMiddleware>();

        return builder;
    }
}
