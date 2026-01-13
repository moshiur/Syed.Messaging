using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Syed.Messaging.ServiceDiscovery;

/// <summary>
/// Consul-based service discovery.
/// </summary>
public class ConsulServiceDiscovery : IServiceDiscovery
{
    private readonly ServiceDiscoveryOptions _options;
    private readonly ILogger<ConsulServiceDiscovery> _logger;
    private readonly HttpClient _httpClient;

    public ConsulServiceDiscovery(
        IOptions<ServiceDiscoveryOptions> options,
        ILogger<ConsulServiceDiscovery> logger,
        HttpClient? httpClient = null)
    {
        _options = options.Value;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();

        if (_options.Consul != null)
        {
            _httpClient.BaseAddress = new Uri(_options.Consul.Address);

            if (!string.IsNullOrEmpty(_options.Consul.Token))
            {
                _httpClient.DefaultRequestHeaders.Add("X-Consul-Token", _options.Consul.Token);
            }
        }
    }

    public async Task<IReadOnlyList<ServiceEndpoint>> ResolveAsync(string serviceName, CancellationToken ct = default)
    {
        try
        {
            var url = $"/v1/health/service/{serviceName}?passing=true";

            if (_options.Consul?.Datacenter != null)
            {
                url += $"&dc={_options.Consul.Datacenter}";
            }

            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var services = await response.Content.ReadFromJsonAsync<List<ConsulServiceEntry>>(ct);

            if (services == null || services.Count == 0)
            {
                _logger.LogWarning("No healthy instances found for service {ServiceName}", serviceName);
                return Array.Empty<ServiceEndpoint>();
            }

            var endpoints = services.Select(s => new ServiceEndpoint
            {
                Host = s.Service?.Address ?? s.Node?.Address ?? string.Empty,
                Port = s.Service?.Port ?? 0,
                IsHealthy = true,
                Metadata = s.Service?.Meta
            }).ToList();

            _logger.LogDebug("Resolved {ServiceName} to {Count} endpoints via Consul", serviceName, endpoints.Count);

            return endpoints;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve service {ServiceName} from Consul", serviceName);
            return Array.Empty<ServiceEndpoint>();
        }
    }

    public async IAsyncEnumerable<IReadOnlyList<ServiceEndpoint>> WatchAsync(
        string serviceName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var lastIndex = "0";
        var lastEndpoints = new List<ServiceEndpoint>();

        while (!ct.IsCancellationRequested)
        {
            List<ServiceEndpoint>? endpoints = null;
            bool shouldYield = false;

            try
            {
                var url = $"/v1/health/service/{serviceName}?passing=true&index={lastIndex}&wait=30s";

                if (_options.Consul?.Datacenter != null)
                {
                    url += $"&dc={_options.Consul.Datacenter}";
                }

                var response = await _httpClient.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                // Update blocking index for long polling
                if (response.Headers.TryGetValues("X-Consul-Index", out var indexValues))
                {
                    lastIndex = indexValues.FirstOrDefault() ?? lastIndex;
                }

                var services = await response.Content.ReadFromJsonAsync<List<ConsulServiceEntry>>(ct);

                endpoints = services?.Select(s => new ServiceEndpoint
                {
                    Host = s.Service?.Address ?? s.Node?.Address ?? string.Empty,
                    Port = s.Service?.Port ?? 0,
                    IsHealthy = true,
                    Metadata = s.Service?.Meta
                }).ToList() ?? new List<ServiceEndpoint>();

                // Only yield if endpoints changed
                if (!EndpointsEqual(endpoints, lastEndpoints))
                {
                    lastEndpoints = endpoints;
                    shouldYield = true;
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error watching service {ServiceName}", serviceName);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }

            // Yield outside of try block
            if (shouldYield && endpoints != null)
            {
                yield return endpoints;
            }
        }
    }

    private static bool EndpointsEqual(IReadOnlyList<ServiceEndpoint> a, IReadOnlyList<ServiceEndpoint> b)
    {
        if (a.Count != b.Count) return false;

        var aHosts = a.Select(e => $"{e.Host}:{e.Port}").OrderBy(h => h).ToList();
        var bHosts = b.Select(e => $"{e.Host}:{e.Port}").OrderBy(h => h).ToList();

        return aHosts.SequenceEqual(bHosts);
    }

    // Consul API response types
    private class ConsulServiceEntry
    {
        public ConsulNode? Node { get; set; }
        public ConsulService? Service { get; set; }
    }

    private class ConsulNode
    {
        public string? Address { get; set; }
    }

    private class ConsulService
    {
        public string? Address { get; set; }
        public int Port { get; set; }
        public Dictionary<string, string>? Meta { get; set; }
    }
}
