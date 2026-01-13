namespace Syed.Messaging;

/// <summary>
/// Configuration options for service mesh/sidecar integration.
/// </summary>
public class MeshOptions
{
    /// <summary>
    /// Whether mesh integration is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The mesh type (istio, linkerd, envoy, none).
    /// </summary>
    public string MeshType { get; set; } = "none";

    /// <summary>
    /// Whether to use mTLS for broker connections through the sidecar.
    /// </summary>
    public bool UseMtls { get; set; } = false;

    /// <summary>
    /// Sidecar proxy address (e.g., localhost:15001 for Istio).
    /// </summary>
    public string? SidecarAddress { get; set; }

    /// <summary>
    /// Headers to propagate through the mesh.
    /// </summary>
    public List<string> PropagatedHeaders { get; set; } = new()
    {
        "x-request-id",
        "x-b3-traceid",
        "x-b3-spanid",
        "x-b3-parentspanid",
        "x-b3-sampled",
        "x-b3-flags",
        "x-ot-span-context",
        "traceparent",
        "tracestate"
    };

    /// <summary>
    /// Traffic policy settings.
    /// </summary>
    public TrafficPolicyOptions TrafficPolicy { get; set; } = new();

    /// <summary>
    /// Retry policy that integrates with mesh retry logic.
    /// </summary>
    public MeshRetryOptions Retry { get; set; } = new();
}

public class TrafficPolicyOptions
{
    /// <summary>
    /// Rate limit (requests per second). 0 = no limit.
    /// </summary>
    public int RateLimitRps { get; set; } = 0;

    /// <summary>
    /// Circuit breaker max concurrent requests.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 100;

    /// <summary>
    /// Circuit breaker max pending requests.
    /// </summary>
    public int MaxPendingRequests { get; set; } = 100;

    /// <summary>
    /// Connection timeout.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);
}

public class MeshRetryOptions
{
    /// <summary>
    /// Let the mesh handle retries (disable application-level retry).
    /// </summary>
    public bool DelegateToMesh { get; set; } = false;

    /// <summary>
    /// Number of retry attempts (if not delegating to mesh).
    /// </summary>
    public int Attempts { get; set; } = 3;

    /// <summary>
    /// Retry on these HTTP status codes (mesh integration).
    /// </summary>
    public List<int> RetryOnStatusCodes { get; set; } = new() { 502, 503, 504 };
}
