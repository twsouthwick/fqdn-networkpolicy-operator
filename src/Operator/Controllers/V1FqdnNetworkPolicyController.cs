using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using k8s;
using k8s.Autorest;
using k8s.Models;
using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Serialization;
using Swick.FqdnNetworkPolicyOperator.Entities;


namespace Swick.FqdnNetworkPolicyOperator.Controllers;

[EntityRbac(typeof(V1NetworkPolicy), Verbs = RbacVerb.Get | RbacVerb.Create | RbacVerb.Update)]
[EntityRbac(typeof(V1FqdnNetworkPolicyEntity), Verbs = RbacVerb.Get | RbacVerb.Update)]
[EntityRbac(typeof(V3GlobalNetworkSet), Verbs = RbacVerb.Get | RbacVerb.Create | RbacVerb.Update | RbacVerb.Delete)]
public class V1FqdnNetworkPolicyController(HttpClient httpClient, IKubernetesClient client, IKubernetes kubernetes, ILogger<V1FqdnNetworkPolicyController> logger)
    : IEntityController<V1FqdnNetworkPolicyEntity>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static Dictionary<string, string> ManagedLabels(V1FqdnNetworkPolicyEntity entity) => new()
    {
        [$"{Constants.ApiGroup}/managed-by"] = "fqdn-networkpolicy-operator",
        [$"{Constants.ApiGroup}/name"] = entity.Name(),
        [$"{Constants.ApiGroup}/namespace"] = entity.Namespace(),
    };

    private static string LabelSelector(V1FqdnNetworkPolicyEntity entity) =>
        $"{Constants.ApiGroup}/managed-by=fqdn-networkpolicy-operator,{Constants.ApiGroup}/name={entity.Name()},{Constants.ApiGroup}/namespace={entity.Namespace()}";

    public async Task<ReconciliationResult<V1FqdnNetworkPolicyEntity>> DeletedAsync(V1FqdnNetworkPolicyEntity entity, CancellationToken cancellationToken)
    {
        var existing = await FindGlobalNetworkSetAsync(entity, cancellationToken);
        if (existing is not null)
        {
            try
            {
                await kubernetes.CustomObjects.DeleteClusterCustomObjectAsync(
                    "projectcalico.org", "v3", "globalnetworksets", existing.Metadata.Name,
                    cancellationToken: cancellationToken);
                logger.LogInformation("Deleted GlobalNetworkSet {Name}", existing.Metadata.Name);
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                // Already gone
            }
        }

        return ReconciliationResult<V1FqdnNetworkPolicyEntity>.Success(entity);
    }

    public async Task<ReconciliationResult<V1FqdnNetworkPolicyEntity>> ReconcileAsync(V1FqdnNetworkPolicyEntity entity, CancellationToken cancellationToken)
    {
        try
        {
            var warnings = new List<string>();
            var egressRules = await GetPeersAsync(entity, warnings, cancellationToken).ToListAsync(cancellationToken);

            var networkPolicyChanged = await ApplyNetworkPolicyAsync(entity, egressRules, cancellationToken);
            var gnsChanged = await ApplyGlobalNetworkSetAsync(entity, egressRules, cancellationToken);
            var changed = networkPolicyChanged|| gnsChanged;

            entity.Status.Ready = true;
            entity.Status.IPCount = egressRules.Sum(r => r.To?.Count ?? 0);
            entity.Status.DomainCount = entity.Spec.Egress
                .SelectMany(e => e.Domains ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            entity.Status.WarningCount = warnings.Count;
            entity.Status.LastReconciled = DateTimeOffset.UtcNow;
            if (changed)
            {
                entity.Status.LastModified = DateTimeOffset.UtcNow;
            }
            entity.Status.Message = warnings.Count == 0
                ? "Success"
                : $"{warnings.Count} warning(s): {string.Join("; ", warnings)}";

            var updatedEntity = await client.UpdateStatusAsync(entity, cancellationToken);

            return ReconciliationResult<V1FqdnNetworkPolicyEntity>.Success(updatedEntity, TimeSpan.FromSeconds(30));
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

            logger.LogError(e, "Error reconciling {Name}", entity.Name());
            return ReconciliationResult<V1FqdnNetworkPolicyEntity>.Failure(entity, e.Message, e, TimeSpan.FromMinutes(2));
        }
    }

    private async Task<bool> ApplyNetworkPolicyAsync(V1FqdnNetworkPolicyEntity entity, List<V1NetworkPolicyEgressRule> egressRules, CancellationToken cancellationToken)
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
                Labels = ManagedLabels(entity),
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

        static V1NetworkPolicySpec GetNetworkPolicySpec(V1FqdnNetworkPolicyEntity entity)
        {
            // Serialize to do a deep copy for now
            return KubernetesJsonSerializer.Deserialize<V1NetworkPolicySpec>(KubernetesJsonSerializer.Serialize(entity.Spec.Policy));
        }

        static string NetworkPolicyName(V1FqdnNetworkPolicyEntity entity) => entity.Name();
    }

    private async Task<bool> ApplyGlobalNetworkSetAsync(V1FqdnNetworkPolicyEntity entity, List<V1NetworkPolicyEgressRule> egressRules, CancellationToken cancellationToken)
    {
        var name = GlobalNetworkSetName(entity);
        var nets = egressRules
            .SelectMany(r => r.To ?? [])
            .Select(p => p.IpBlock?.Cidr)
            .OfType<string>()
            .Distinct()
            .Order()
            .ToList();

        var gns = new V3GlobalNetworkSet
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                Labels = ManagedLabels(entity),
            },
            Spec = new V3GlobalNetworkSet.GlobalNetworkSetSpec { Nets = nets },
        };

        var existingGns = await FindGlobalNetworkSetAsync(entity, cancellationToken);

        if (existingGns is not null)
        {
            if (existingGns.Spec.Nets.Order().SequenceEqual(nets))
            {
                logger.LogDebug("GlobalNetworkSet {Name} is unchanged, skipping update", name);
                return false;
            }

            gns.Metadata.ResourceVersion = existingGns.Metadata?.ResourceVersion;

            await kubernetes.CustomObjects.ReplaceClusterCustomObjectAsync(
                gns, "projectcalico.org", "v3", "globalnetworksets", name,
                cancellationToken: cancellationToken);
            logger.LogInformation("Updated GlobalNetworkSet {Name} with {NetCount} net(s)", name, nets.Count);
            return true;
        }

        try
        {
            await kubernetes.CustomObjects.CreateClusterCustomObjectAsync(
                gns, "projectcalico.org", "v3", "globalnetworksets",
                cancellationToken: cancellationToken);
            logger.LogInformation("Created GlobalNetworkSet {Name} with {NetCount} net(s)", name, nets.Count);
            return true;
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogDebug("GlobalNetworkSet CRD not available, skipping");
            return false;
        }
    }

    private static string GlobalNetworkSetName(V1FqdnNetworkPolicyEntity entity) => $"fqdn-{entity.Namespace()}-{entity.Name()}";

    private async Task<V3GlobalNetworkSet?> FindGlobalNetworkSetAsync(V1FqdnNetworkPolicyEntity entity, CancellationToken cancellationToken)
    {
        try
        {
            var result = await kubernetes.CustomObjects.ListClusterCustomObjectAsync<V3GlobalNetworkSet.List>(
                "projectcalico.org", "v3", "globalnetworksets",
                labelSelector: LabelSelector(entity),
                cancellationToken: cancellationToken);
            return result.Items.FirstOrDefault();
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            // CRD not installed
            return null;
        }
    }

    private async IAsyncEnumerable<V1NetworkPolicyEgressRule> GetPeersAsync(V1FqdnNetworkPolicyEntity entity, List<string> warnings, [EnumeratorCancellation] CancellationToken cancellationToken)
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

            if (egress.ExternalProvider is not null)
            {
                await foreach (var rule in GetEgressRuleFromProviderAsync(entity, egress.ExternalProvider, warnings, cancellationToken))
                {
                    yield return rule;
                }
            }
        }
    }

    private async IAsyncEnumerable<V1NetworkPolicyEgressRule> GetEgressRuleFromProviderAsync(V1FqdnNetworkPolicyEntity entity, V1FqdnNetworkPolicyEntity.ExternalProviderRef provider, List<string> warnings, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching addresses from external provider {ServiceName}:{Port} for {Name}", provider.ServiceName, provider.Port, entity.Name());

        var envelope = await httpClient.GetFromJsonAsync<ProviderEnvelope>($"http://{provider.ServiceName}.{entity.Namespace()}:{provider.Port}{provider.Path}", cancellationToken);

        if (envelope is null)
        {
            var msg = $"Null response from external provider service '{provider.ServiceName}'";
            logger.LogWarning("Received null response from external provider {ServiceName} for {Name}", provider.ServiceName, entity.Name());
            warnings.Add(msg);
            yield break;
        }

        foreach (var response in envelope.Egress)
        {
            var peers = await GetIpNetworksAsync(response.Addresses, warnings, cancellationToken)
                .Order(IPNetworkComparer.Instance)
                .Select(network => new V1NetworkPolicyPeer { IpBlock = new V1IPBlock { Cidr = network.ToString() } })
                .ToListAsync(cancellationToken);

            yield return new V1NetworkPolicyEgressRule
            {
                To = peers,
                Ports = response.Ports
            };
        }
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

    private sealed class ProviderEnvelope
    {
        [JsonPropertyName("egress")]
        public ProviderEgressRule[] Egress { get; set; } = [];
    }

    private sealed class ProviderEgressRule
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