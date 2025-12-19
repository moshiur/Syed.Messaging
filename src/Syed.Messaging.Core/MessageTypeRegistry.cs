using System.Collections.Concurrent;
using System.Reflection;

namespace Syed.Messaging;

/// <summary>
/// Default implementation of IMessageTypeRegistry.
/// Thread-safe for reads, mutable during startup registration.
/// </summary>
public sealed class MessageTypeRegistry : IMessageTypeRegistry
{
    private readonly ConcurrentDictionary<(string TypeKey, string? Version), Type> _keyToType = new();
    private readonly ConcurrentDictionary<Type, (string TypeKey, string? Version)> _typeToKey = new();

    /// <inheritdoc />
    public void Register<TMessage>(string? typeKey = null, string? version = null)
        => Register(typeof(TMessage), typeKey, version);

    /// <inheritdoc />
    public void Register(Type messageType, string? typeKey = null, string? version = null)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        // If no typeKey provided, check for attribute or use type name
        if (string.IsNullOrWhiteSpace(typeKey))
        {
            var attr = messageType.GetCustomAttribute<MessageTypeAttribute>();
            if (attr is not null)
            {
                typeKey = attr.TypeKey;
                version ??= attr.Version;
            }
            else
            {
                typeKey = messageType.FullName ?? messageType.Name;
            }
        }

        var key = (typeKey, version);

        if (!_keyToType.TryAdd(key, messageType))
        {
            var existing = _keyToType[key];
            if (existing != messageType)
            {
                throw new InvalidOperationException(
                    $"Type key '{typeKey}' (version: {version ?? "null"}) is already registered to type '{existing.FullName}'. " +
                    $"Cannot register to '{messageType.FullName}'.");
            }
            // Same type re-registered, ignore
            return;
        }

        _typeToKey.TryAdd(messageType, key);
    }

    /// <inheritdoc />
    public Type? Resolve(string typeKey, string? version = null)
    {
        TryResolve(typeKey, version, out var type);
        return type;
    }

    /// <inheritdoc />
    public bool TryResolve(string typeKey, string? version, out Type? type)
    {
        // Try exact match first
        if (_keyToType.TryGetValue((typeKey, version), out type))
        {
            return true;
        }

        // If version specified but not found, try without version (fallback)
        if (version is not null && _keyToType.TryGetValue((typeKey, null), out type))
        {
            return true;
        }

        type = null;
        return false;
    }

    /// <inheritdoc />
    public (string TypeKey, string? Version) GetTypeKey(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        if (_typeToKey.TryGetValue(messageType, out var result))
        {
            return result;
        }

        throw new InvalidOperationException(
            $"Type '{messageType.FullName}' is not registered in the message type registry. " +
            $"Call Register<{messageType.Name}>() during startup.");
    }

    /// <inheritdoc />
    public (string TypeKey, string? Version) GetTypeKey<TMessage>()
        => GetTypeKey(typeof(TMessage));

    /// <inheritdoc />
    public bool TryGetTypeKey(Type messageType, out string? typeKey, out string? version)
    {
        if (_typeToKey.TryGetValue(messageType, out var result))
        {
            typeKey = result.TypeKey;
            version = result.Version;
            return true;
        }

        typeKey = null;
        version = null;
        return false;
    }

    /// <summary>
    /// Auto-registers all types with [MessageType] attribute from the given assemblies.
    /// </summary>
    public void RegisterFromAssemblies(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                var attr = type.GetCustomAttribute<MessageTypeAttribute>();
                if (attr is not null)
                {
                    Register(type, attr.TypeKey, attr.Version);
                }
            }
        }
    }

    /// <summary>
    /// Auto-registers all types with [MessageType] attribute from the given assembly.
    /// </summary>
    public void RegisterFromAssembly(Assembly assembly)
        => RegisterFromAssemblies(assembly);
}
