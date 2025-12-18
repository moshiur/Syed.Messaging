using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Retry;

namespace Syed.Messaging.Core;

public static class ResiliencePipelineExtensions
{
    /// <summary>
    /// Adds a default resilience pipeline for message processing.
    /// Registers a ResiliencePipeline as a keyed service or singleton.
    /// </summary>
    public static IServiceCollection AddMessageResilience(this IServiceCollection services, string pipelineKey = "message-processing")
    {
        // Define the pipeline
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .Build();

        // Register it. 
        // Ideally we'd use ResiliencePipelineRegistry if available, but for now we can register the pipeline directly.
        // We register it as a keyed service if .NET 8, or just a singleton wrapper.
        
        // Simple approach: Register a provider/registry
        services.AddSingleton<ResiliencePipeline>(pipeline); // Register as default for now
        
        return services;
    }
}
