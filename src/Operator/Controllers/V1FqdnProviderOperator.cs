using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using k8s.Models;
using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Serialization;
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
            var warnings = new List<string>();
            var egressRules = await GetPeersAsync(entity, warnings, cancellationToken).ToListAsync(cancellationToken);

            var changed = await ApplyNetworkPolicyAsync(entity, egressRules, cancellationToken);

            entity.Status.Ready = true;
            entity.Status.IpCount = egressRules.Sum(r => r.To?.Count ?? 0);
            entity.Status.DomainCount = entity.Spec.Egress
                .SelectMany(e => e.Domains ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            entity.Status.Warnings = warnings.Count;
            entity.Status.LastReconciled = DateTimeOffset.UtcNow;
            if (changed)
            {
                entity.Status.LastModified = DateTimeOffset.UtcNow;
            }
            entity.Status.Message = warnings.Count == 0
                ? "Success"
                : $"{warnings.Count} warning(s): {string.Join("; ", warnings)}";

            var updatedEntity = await client.UpdateStatusAsync(entity, cancellationToken);

            return ReconciliationResult<V1FqdnProviderEntity>.Success(updatedEntity, TimeSpan.FromSeconds(30));
        }
        catch (Exception e)
        {
            entity.Status.Ready = false;
            entity.Status.Message = "Error: " + e.Message;

            try
            {
                entity = await client.UpdateStatusAsync(entity, cancellationToken);
            }
            catch
            {
                /* best effort */
            }

            logger.LogError(e, "Error reconciling provider {Name}", entity.Name());
            return ReconciliationResult<V1FqdnProviderEntity>.Failure(entity, e.Message, e, TimeSpan.FromMinutes(2));
        }
    }

    private async Task<bool> ApplyNetworkPolicyAsync(V1FqdnProviderEntity entity, List<V1NetworkPolicyEgressRule> egressRules, CancellationToken cancellationToken)
    {
        var policyName = NetworkPolicyName(entity);

        var spec = GetNetworkPolicySpec(entity);

        spec.Egress = spec.Egress is null
            ? egressRules
            : [.. spec.Egress, .. egressRules];

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

        var existing = await client.GetAsync<V1NetworkPolicy>(policyName, entity.Namespace(), cancellationToken);

        if (existing is not null)
        {
            var existingSpecJson = KubernetesJsonSerializer.Serialize(existing.Spec);
            var newSpecJson = KubernetesJsonSerializer.Serialize(spec);

            if (existingSpecJson == newSpecJson)
            {
                logger.LogDebug("NetworkPolicy {PolicyName} is unchanged, skipping update", policyName);
                return false;
            }

            policy.Metadata.ResourceVersion = existing.Metadata.ResourceVersion;
            await client.UpdateAsync(policy, cancellationToken);
        }
        else
        {
            await client.CreateAsync(policy, cancellationToken);
        }

        logger.LogInformation("Saved NetworkPolicy {PolicyName} with {RuleCount} egress rule(s) for provider {Name}", policyName, egressRules.Count, entity.Name());
        return true;

        static V1NetworkPolicySpec GetNetworkPolicySpec(V1FqdnProviderEntity entity)
        {
            // Serialize to do a deep copy for now
            return KubernetesJsonSerializer.Deserialize<V1NetworkPolicySpec>(KubernetesJsonSerializer.Serialize(entity.Spec.Policy));
        }

        static string NetworkPolicyName(V1FqdnProviderEntity entity) => entity.Name();
    }

    private async IAsyncEnumerable<V1NetworkPolicyEgressRule> GetPeersAsync(V1FqdnProviderEntity entity, List<string> warnings, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var egress in entity.Spec.Egress)
        {
            if (egress.Domains is not null)
            {
                var peers = await GetIpNetworksAsync(egress.Domains, warnings, cancellationToken)
                    .Order(IPNetworkComparer.Instance)
                    .Select(network => new V1NetworkPolicyPeer { IpBlock = new V1IPBlock { Cidr = network.ToString() } })
                    .ToListAsync(cancellationToken);

                yield return new V1NetworkPolicyEgressRule
                {
                    To = peers,
                    Ports = egress.Ports
                };
            }

            if (egress.Provider is not null)
            {
                var rule = await GetEgressRuleFromProviderAsync(entity, egress.Provider, warnings, cancellationToken);
                if (rule is not null)
                {
                    yield return rule;
                }
            }
        }
    }

    private async Task<V1NetworkPolicyEgressRule?> GetEgressRuleFromProviderAsync(V1FqdnProviderEntity entity, V1FqdnProviderEntity.Provider provider, List<string> warnings, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reconciling provider {Name} with service {ServiceName}:{Port}", entity.Name(), provider.ServiceName, provider.Port);

        var response = await httpClient.GetFromJsonAsync<ProviderResponse>($"http://{provider.ServiceName}.{entity.Namespace()}:{provider.Port}{provider.Path}", cancellationToken);

        if (response is null)
        {
            var msg = $"Null response from provider service '{provider.ServiceName}'";
            logger.LogWarning("Received null response from provider {Name}", entity.Name());
            warnings.Add(msg);
            return null;
        }

        var peers = await GetIpNetworksAsync(response.Addresses, warnings, cancellationToken)
            .Order(IPNetworkComparer.Instance)
            .Select(network => new V1NetworkPolicyPeer { IpBlock = new V1IPBlock { Cidr = network.ToString() } })
            .ToListAsync(cancellationToken);

        return new V1NetworkPolicyEgressRule
        {
            To = peers,
            Ports = response.Ports
        };
    }

    private async IAsyncEnumerable<IPNetwork> GetIpNetworksAsync(IEnumerable<string> ipOrAddresses, List<string> warnings, [EnumeratorCancellation] CancellationToken token)
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
                catch (System.Net.Sockets.SocketException ex) when ((uint)ex.ErrorCode == 0xFFFDFFFF)
                {
                    warnings.Add($"Could not resolve '{ipOrAddress}'");
                    logger.LogWarning("Could not resolve '{Fqdn}' from provider response", ipOrAddress);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to resolve '{ipOrAddress}': {ex.Message}");
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
        [JsonPropertyName("addresses")]
        public string[] Addresses { get; set; } = [];

        [JsonPropertyName("ports")]
        public V1NetworkPolicyPort[] Ports { get; set; } = [];
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