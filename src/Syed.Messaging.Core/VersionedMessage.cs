namespace Syed.Messaging;

/// <summary>
/// Wrapper for versioned messages to support schema evolution.
/// </summary>
/// <typeparam name="T">The message payload type.</typeparam>
public class VersionedMessage<T> where T : class
{
    /// <summary>
    /// The message payload.
    /// </summary>
    public T Payload { get; }

    /// <summary>
    /// The schema version of this message.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Original version if this message was upgraded from an older schema.
    /// </summary>
    public string? OriginalVersion { get; init; }

    public VersionedMessage(T payload, string version)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Version = version ?? throw new ArgumentNullException(nameof(version));
    }

    /// <summary>
    /// Creates a new version of this message with an upgraded payload.
    /// </summary>
    public VersionedMessage<TNew> Upgrade<TNew>(TNew newPayload, string newVersion) where TNew : class
    {
        return new VersionedMessage<TNew>(newPayload, newVersion)
        {
            OriginalVersion = OriginalVersion ?? Version
        };
    }
}

/// <summary>
/// Helper methods for versioned messages.
/// </summary>
public static class VersionedMessageExtensions
{
    /// <summary>
    /// Wraps a message with version information.
    /// </summary>
    public static VersionedMessage<T> WithVersion<T>(this T message, string version) where T : class
    {
        return new VersionedMessage<T>(message, version);
    }

    /// <summary>
    /// Checks if the message needs upgrade from an older version.
    /// </summary>
    public static bool NeedsUpgrade<T>(this VersionedMessage<T> message, string currentVersion) where T : class
    {
        return !string.Equals(message.Version, currentVersion, StringComparison.OrdinalIgnoreCase);
    }
}
