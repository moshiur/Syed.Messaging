using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Syed.Messaging.ServiceDiscovery;

/// <summary>
/// Kubernetes DNS-based service discovery.
/// Resolves services using Kubernetes DNS (servicename.namespace.svc.cluster.local).
/// </summary>
public class KubernetesDnsServiceDiscovery : IServiceDiscovery
{
    private readonly ServiceDiscoveryOptions _options;
    private readonly ILogger<KubernetesDnsServiceDiscovery> _logger;

    public KubernetesDnsServiceDiscovery(
        IOptions<ServiceDiscoveryOptions> options, 
        ILogger<KubernetesDnsServiceDiscovery> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ServiceEndpoint>> ResolveAsync(string serviceName, CancellationToken ct = default)
    {
        try
        {
            // Parse service name (format: name or name.namespace or full FQDN)
            var fqdn = NormalizeFqdn(serviceName);

            var hostEntry = await Dns.GetHostEntryAsync(fqdn, ct);

            var endpoints = hostEntry.AddressList
                .Select(ip => new ServiceEndpoint
                {
                    Host = ip.ToString(),
                    Port = ExtractPort(serviceName),
                    IsHealthy = true,
                    Metadata = new Dictionary<string, string>
                    {
                        ["resolved_from"] = fqdn,
                        ["address_family"] = ip.AddressFamily.ToString()
                    }
                })
                .ToList();

            _logger.LogDebug("Resolved {ServiceName} to {Count} endpoints", serviceName, endpoints.Count);

            return endpoints;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve service {ServiceName}", serviceName);
            return Array.Empty<ServiceEndpoint>();
        }
    }

    public async IAsyncEnumerable<IReadOnlyList<ServiceEndpoint>> WatchAsync(
        string serviceName, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var lastEndpoints = new List<ServiceEndpoint>();

        while (!ct.IsCancellationRequested)
        {
            var endpoints = await ResolveAsync(serviceName, ct);

            // Only yield if endpoints changed
            if (!EndpointsEqual(endpoints, lastEndpoints))
            {
                lastEndpoints = endpoints.ToList();
                yield return endpoints;
            }

            try
            {
                await Task.Delay(_options.RefreshInterval, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private static string NormalizeFqdn(string serviceName)
    {
        // If already FQDN, return as-is
        if (serviceName.EndsWith(".svc.cluster.local"))
            return serviceName;

        // If includes namespace (service.namespace), add suffix
        if (serviceName.Contains('.'))
            return $"{serviceName}.svc.cluster.local";

        // Otherwise, assume default namespace
        return $"{serviceName}.default.svc.cluster.local";
    }

    private static int ExtractPort(string serviceName)
    {
        // Check for port in format service:port
        var colonIndex = serviceName.LastIndexOf(':');
        if (colonIndex > 0 && int.TryParse(serviceName[(colonIndex + 1)..], out var port))
        {
            return port;
        }

        // Default ports for common services
        if (serviceName.Contains("rabbitmq")) return 5672;
        if (serviceName.Contains("kafka")) return 9092;
        if (serviceName.Contains("redis")) return 6379;

        return 0;
    }

    private static bool EndpointsEqual(IReadOnlyList<ServiceEndpoint> a, IReadOnlyList<ServiceEndpoint> b)
    {
        if (a.Count != b.Count) return false;

        var aHosts = a.Select(e => e.Host).OrderBy(h => h).ToList();
        var bHosts = b.Select(e => e.Host).OrderBy(h => h).ToList();

        return aHosts.SequenceEqual(bHosts);
    }
}

/// <summary>
/// Static DNS-based service discovery (fallback for non-K8s environments).
/// </summary>
public class DnsServiceDiscovery : IServiceDiscovery
{
    private readonly ILogger<DnsServiceDiscovery> _logger;
    private readonly ServiceDiscoveryOptions _options;

    public DnsServiceDiscovery(
        IOptions<ServiceDiscoveryOptions> options,
        ILogger<DnsServiceDiscovery> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ServiceEndpoint>> ResolveAsync(string serviceName, CancellationToken ct = default)
    {
        try
        {
            // Parse host:port
            var parts = serviceName.Split(':');
            var host = parts[0];
            var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 0;

            var hostEntry = await Dns.GetHostEntryAsync(host, ct);

            return hostEntry.AddressList
                .Select(ip => new ServiceEndpoint
                {
                    Host = ip.ToString(),
                    Port = port,
                    IsHealthy = true
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve DNS for {ServiceName}", serviceName);
            return Array.Empty<ServiceEndpoint>();
        }
    }

    public async IAsyncEnumerable<IReadOnlyList<ServiceEndpoint>> WatchAsync(
        string serviceName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            yield return await ResolveAsync(serviceName, ct);

            try
            {
                await Task.Delay(_options.RefreshInterval, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
