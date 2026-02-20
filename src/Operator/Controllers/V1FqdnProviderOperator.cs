using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using k8s.Models;
using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.KubernetesClient;
using Swick.FqdnNetworkPolicyOperator.Entities;

namespace Swick.FqdnNetworkPolicyOperator.Controllers;

[EntityRbac(typeof(V1NetworkPolicy), Verbs = RbacVerb.Get | RbacVerb.Create | RbacVerb.Update)]
[EntityRbac(typeof(V1FqdnProviderEntity), Verbs = RbacVerb.Get | RbacVerb.Update)]
public class V1FqdnProviderOperator(HttpClient httpClient, IKubernetesClient client, ILogger<V1FqdnProviderOperator> logger)
    : IEntityController<V1FqdnProviderEntity>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<ReconciliationResult<V1FqdnProviderEntity>> DeletedAsync(V1FqdnProviderEntity entity, CancellationToken cancellationToken)
        => Task.FromResult(ReconciliationResult<V1FqdnProviderEntity>.Success(entity));

    public async Task<ReconciliationResult<V1FqdnProviderEntity>> ReconcileAsync(V1FqdnProviderEntity entity, CancellationToken cancellationToken)
    {
        try
        {
            var ipNetworks = await GetPeersAsync(entity, cancellationToken).ToListAsync(cancellationToken);

            await ApplyNetworkPolicyAsync(entity, ipNetworks, cancellationToken);

            entity.Status.IpCount = ipNetworks.Count;
            entity.Status.LastUpdated = DateTimeOffset.UtcNow;
            
            await client.UpdateStatusAsync(entity, cancellationToken);

            return ReconciliationResult<V1FqdnProviderEntity>.Success(entity, TimeSpan.FromSeconds(30));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error reconciling provider {Name}", entity.Name());
            return ReconciliationResult<V1FqdnProviderEntity>.Failure(entity, e.Message, e, TimeSpan.FromMinutes(2));
        }
    }

    private async Task ApplyNetworkPolicyAsync(V1FqdnProviderEntity entity, List<V1NetworkPolicyEgressRule> ipNetworkRules, CancellationToken cancellationToken)
    {
        var policyName = NetworkPolicyName(entity);

        var spec = entity.Spec.Policy;

        spec.Egress = spec.Egress is null 
            ? ipNetworkRules
            : [.. spec.Egress, .. ipNetworkRules];

        var policy = new V1NetworkPolicy
        {
            Metadata = new V1ObjectMeta
            {
                Name = policyName,
                NamespaceProperty = entity.Namespace(),
                OwnerReferences =
                [
                    new V1OwnerReference
                    {
                        ApiVersion = entity.ApiVersion,
                        Kind = entity.Kind,
                        Name = entity.Name(),
                        Uid = entity.Uid(),
                    }
                ],
            },
            Spec = spec,
        };

        await client.SaveAsync(policy, cancellationToken);

        logger.LogInformation("Saved NetworkPolicy {PolicyName} with {IpCount} IP block(s) for provider {Name}", policyName, ipNetworkRules.Count, entity.Name());
    }

    private static string NetworkPolicyName(V1FqdnProviderEntity entity) => entity.Name();

    private async IAsyncEnumerable<V1NetworkPolicyEgressRule> GetPeersAsync(V1FqdnProviderEntity entity, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var egress in entity.Spec.Egress)
        {
            var networks = await GetIpNetworksAsync(entity, egress.To, cancellationToken)
                .Order(IPNetworkComparer.Instance)
                .Select(network => new V1NetworkPolicyPeer { IpBlock = new V1IPBlock { Cidr = network.ToString() } })
                .ToListAsync(cancellationToken);

            yield return new V1NetworkPolicyEgressRule
            {
                To = networks,
                Ports = egress.Ports
            };
        }
    }

    private async IAsyncEnumerable<IPNetwork> GetIpNetworksAsync(V1FqdnProviderEntity entity, V1FqdnProviderEntity.EgressRuleItem[] items, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (item.Domains is not null)
            {
                await foreach (var ip in GetIpNetworksAsync(item.Domains, cancellationToken))
                {
                    yield return ip;
                }
            }

            if (item.Providers is not null)
            {
                await foreach (var ip in GetIPNetworksAsync(entity, item.Providers, cancellationToken))
                {
                    yield return ip;
                }
            }
        }
    }

    private async IAsyncEnumerable<IPNetwork> GetIPNetworksAsync(V1FqdnProviderEntity entity, V1FqdnProviderEntity.Provider[] providers, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
        {
            logger.LogInformation("Reconciling provider {Name} with service {ServiceName}:{Port}", entity.Name(), provider.ServiceName, provider.Port);

            var response = await httpClient.GetFromJsonAsync<ProviderResponse>($"http://{provider.ServiceName}.{entity.Namespace()}:{provider.Port}{provider.Path}", cancellationToken);

            if (response is null)
            {
                logger.LogWarning("Received null response from provider {Name}", entity.Name());
                continue;
            }

            await foreach (var ip in GetIpNetworksAsync([.. response.Ips, .. response.Fqdns], cancellationToken))
            {
                yield return ip;
            }
        }
    }

    private async IAsyncEnumerable<IPNetwork> GetIpNetworksAsync(IEnumerable<string> ipOrAddresses, [EnumeratorCancellation] CancellationToken token)
    {
        foreach (var ipOrAddress in ipOrAddresses)
        {
            if (IPAddress.TryParse(ipOrAddress, out var address))
            {
                yield return new IPNetwork(address, 32);
            }
            else if (IPNetwork.TryParse(ipOrAddress, out var network))
            {
                yield return network;
            }
            else
            {
                IPAddress[] addresses = [];

                try
                {
                    addresses = await Dns.GetHostAddressesAsync(ipOrAddress, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to resolve FQDN '{Fqdn}' from provider response", ipOrAddress);
                }

                foreach (var a in addresses)
                {
                    yield return new IPNetwork(a, 32);
                }
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