namespace Syed.Messaging;

/// <summary>
/// Interface for schema registry operations to support message schema validation and evolution.
/// </summary>
public interface ISchemaRegistry
{
    /// <summary>
    /// Registers a schema for a message type.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="version">Schema version.</param>
    /// <param name="schemaJson">JSON schema definition.</param>
    Task RegisterAsync<T>(string version, string schemaJson, CancellationToken ct = default);

    /// <summary>
    /// Gets the schema for a message type and version.
    /// </summary>
    /// <param name="messageType">The message type key.</param>
    /// <param name="version">Schema version (null for latest).</param>
    Task<string?> GetSchemaAsync(string messageType, string? version = null, CancellationToken ct = default);

    /// <summary>
    /// Validates a message payload against its registered schema.
    /// </summary>
    /// <param name="messageType">The message type key.</param>
    /// <param name="version">Schema version.</param>
    /// <param name="payload">The message payload as JSON bytes.</param>
    /// <returns>True if valid; otherwise false with validation errors.</returns>
    Task<SchemaValidationResult> ValidateAsync(string messageType, string version, byte[] payload, CancellationToken ct = default);

    /// <summary>
    /// Checks if a new schema version is compatible with the previous version.
    /// </summary>
    Task<SchemaCompatibilityResult> CheckCompatibilityAsync(string messageType, string newVersion, string newSchemaJson, CancellationToken ct = default);
}

public record SchemaValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public record SchemaCompatibilityResult(bool IsCompatible, CompatibilityLevel Level, IReadOnlyList<string> BreakingChanges);

public enum CompatibilityLevel
{
    /// <summary>New schema can read data written by old schema.</summary>
    Backward,

    /// <summary>Old schema can read data written by new schema.</summary>
    Forward,

    /// <summary>Both backward and forward compatible.</summary>
    Full,

    /// <summary>No compatibility guarantees.</summary>
    None
}
