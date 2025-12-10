namespace Syed.Messaging;

public enum RetryBackoff
{
    Fixed,
    Linear,
    Exponential
}

public sealed class RetryPolicy
{
    public int MaxRetries { get; init; } = 3;
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(5);
    public RetryBackoff Backoff { get; init; } = RetryBackoff.Exponential;
}
