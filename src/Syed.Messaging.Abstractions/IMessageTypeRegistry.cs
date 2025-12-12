namespace Syed.Messaging;

/// <summary>
/// Safe registry mapping message type keys to CLR types.
/// Replaces Type.GetType() for safe, versionable deserialization.
/// </summary>
public interface IMessageTypeRegistry
{
    /// <summary>
    /// Registers a message type with a type key and optional version.
    /// </summary>
    void Register<TMessage>(string? typeKey = null, string? version = null);

    /// <summary>
    /// Registers a message type with a type key and optional version.
    /// </summary>
    void Register(Type messageType, string? typeKey = null, string? version = null);

    /// <summary>
    /// Resolves a CLR type from a type key and optional version.
    /// Returns null if not found.
    /// </summary>
    Type? Resolve(string typeKey, string? version = null);

    /// <summary>
    /// Gets the type key and version for a CLR type.
    /// Throws if not registered.
    /// </summary>
    (string TypeKey, string? Version) GetTypeKey(Type messageType);

    /// <summary>
    /// Gets the type key and version for a message type.
    /// Throws if not registered.
    /// </summary>
    (string TypeKey, string? Version) GetTypeKey<TMessage>();

    /// <summary>
    /// Tries to resolve a CLR type from a type key and version.
    /// Returns false if not found.
    /// </summary>
    bool TryResolve(string typeKey, string? version, out Type? type);

    /// <summary>
    /// Tries to get the type key for a CLR type.
    /// Returns false if not registered.
    /// </summary>
    bool TryGetTypeKey(Type messageType, out string? typeKey, out string? version);
}
