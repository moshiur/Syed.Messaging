namespace Syed.Messaging;

public static class MessageVersioning
{
    public const string VersionHeaderName = "message-version";

    public static void SetVersion(IDictionary<string, string> headers, string version)
        => headers[VersionHeaderName] = version;

    public static string GetVersion(IDictionary<string, string> headers, string defaultVersion = "1")
        => headers.TryGetValue(VersionHeaderName, out var v) && !string.IsNullOrEmpty(v) ? v : defaultVersion;
}
