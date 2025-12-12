namespace Syed.Messaging;

/// <summary>
/// Attribute to specify a message type key and version for auto-registration.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MessageTypeAttribute : Attribute
{
    /// <summary>
    /// The unique type key for this message type.
    /// </summary>
    public string TypeKey { get; }

    /// <summary>
    /// Optional version for this message type.
    /// </summary>
    public string? Version { get; }

    public MessageTypeAttribute(string typeKey, string? version = null)
    {
        TypeKey = typeKey ?? throw new ArgumentNullException(nameof(typeKey));
        Version = version;
    }
}
