using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using k8s.Models;
using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.KubernetesClient;
using Swick.FqdnNetworkPolicyOperator.Entities;

namespace Swick.FqdnNetworkPolicyOperator.Controllers;

[EntityRbac(typeof(V1Secret), Verbs = RbacVerb.Get | RbacVerb.Create | RbacVerb.Delete)]
[EntityRbac(typeof(V1FqdnProviderEntity), Verbs = RbacVerb.All)]
public class V1FqdnProviderOperator(IHttpClientFactory httpClientFactory, IKubernetesClient client, ILogger<V1FqdnProviderOperator> logger)
    : IEntityController<V1FqdnProviderEntity>
{
    public Task<ReconciliationResult<V1FqdnProviderEntity>> DeletedAsync(V1FqdnProviderEntity entity, CancellationToken cancellationToken)
    {
        return Task.FromResult(ReconciliationResult<V1FqdnProviderEntity>.Success(entity));
    }

    public async Task<ReconciliationResult<V1FqdnProviderEntity>> ReconcileAsync(V1FqdnProviderEntity entity, CancellationToken cancellationToken)
    {
        using var httpClient = httpClientFactory.CreateClient();

        logger.LogInformation("Reconciling provider {Name} with service {ServiceName}:{Port}", entity.Name(), entity.Spec.ServiceName, entity.Spec.Port);

        try
        {
            var response = await httpClient.GetFromJsonAsync<ProviderResponse>($"http://{entity.Spec.ServiceName}.{entity.Namespace()}:{entity.Spec.Port}{entity.Spec.Path}", cancellationToken);

            if (response is null)
            {
                logger.LogWarning("Received null response from provider {Name}", entity.Name());
                return ReconciliationResult<V1FqdnProviderEntity>.Failure(entity, "Received null response from provider", requeueAfter: TimeSpan.FromSeconds(10));
            }

            var ips = await GetIpNetworksAsync(response, cancellationToken).ToListAsync(cancellationToken);

            foreach (var ip in ips.Order(IPNetworkComparer.Instance))
            {
                logger.LogInformation("Provider {Name} returned IP network: {Ip}", entity.Name(), ip);
            }

            return ReconciliationResult<V1FqdnProviderEntity>.Success(entity, TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reconciling provider {Name}", entity.Name());
            return ReconciliationResult<V1FqdnProviderEntity>.Failure(entity, ex.Message, ex, TimeSpan.FromSeconds(10));
        }
    }

    private async IAsyncEnumerable<IPNetwork> GetIpNetworksAsync(ProviderResponse response, [EnumeratorCancellation] CancellationToken token)
    {
        foreach (var ip in response.Ips)
        {
            if (IPAddress.TryParse(ip, out var address))
            {
                yield return new IPNetwork(address, 32);
            }
            else if (IPNetwork.TryParse(ip, out var network))
            {
                yield return network;
            }
            else
            {
                logger.LogWarning("Invalid IP address '{Ip}' from provider response", ip);
            }
        }

        foreach (var fqdn in response.Fqdns)
        {
            IPAddress[] addresses = [];

            try
            {
                addresses = await Dns.GetHostAddressesAsync(fqdn, token);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve FQDN '{Fqdn}' from provider response", fqdn);
            }


            foreach (var address in addresses)
            {
                yield return new IPNetwork(address, 32);
            }
        }
    }

    private sealed class ProviderResponse
    {
        [JsonPropertyName("domains")]
        public string[] Fqdns { get; set; } = [];

        [JsonPropertyName("ips")]
        public string[] Ips { get; set; } = [];
    }

    private sealed class IPNetworkComparer : IComparer<IPNetwork>
    {
        public static readonly IPNetworkComparer Instance = new();

        public int Compare(IPNetwork x, IPNetwork y)
        {
            Span<byte> xBytes = stackalloc byte[16];
            Span<byte> yBytes = stackalloc byte[16];

            x.BaseAddress.TryWriteBytes(xBytes, out int xLen);
            y.BaseAddress.TryWriteBytes(yBytes, out int yLen);

            // IPv4 before IPv6
            int cmp = xLen.CompareTo(yLen);
            if (cmp != 0) return cmp;

            cmp = xBytes[..xLen].SequenceCompareTo(yBytes[..yLen]);
            if (cmp != 0) return cmp;

            return x.PrefixLength.CompareTo(y.PrefixLength);
        }
    }
}