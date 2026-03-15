using System.Net;

namespace Swick.FqdnNetworkPolicyOperator.Services;

public class GlobalNetworkSetManager(IKubernetes kubernetes, ILogger<GlobalNetworkSetManager> logger)
{
    public async Task<bool> ApplyAsync(V1FqdnNetworkPolicyEntity entity, List<V1NetworkPolicyEgressRule> egressRules, CancellationToken cancellationToken)
    {
        var name = NameFor(entity);
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
                Labels = ManagedLabels.For(entity),
            },
            Spec = new V3GlobalNetworkSet.GlobalNetworkSetSpec { Nets = nets },
        };

        var existing = await FindAsync(entity, cancellationToken);

        if (existing is not null)
        {
            if (existing.Spec.Nets.Order().SequenceEqual(nets))
            {
                logger.LogDebug("GlobalNetworkSet {Name} is unchanged, skipping update", existing.Metadata.Name);
                return false;
            }

            gns.Metadata.Name = existing.Metadata.Name;
            gns.Metadata.ResourceVersion = existing.Metadata.ResourceVersion;

            await kubernetes.CustomObjects.ReplaceClusterCustomObjectAsync(
                gns, "projectcalico.org", "v3", "globalnetworksets", existing.Metadata.Name,
                cancellationToken: cancellationToken);
            logger.LogInformation("Updated GlobalNetworkSet {Name} with {NetCount} net(s)", existing.Metadata.Name, nets.Count);
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

    public async Task DeleteAsync(V1FqdnNetworkPolicyEntity entity, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(entity, cancellationToken);
        if (existing is null)
        {
            return;
        }

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

    private async Task<V3GlobalNetworkSet?> FindAsync(V1FqdnNetworkPolicyEntity entity, CancellationToken cancellationToken)
    {
        try
        {
            var result = await kubernetes.CustomObjects.ListClusterCustomObjectAsync<V3GlobalNetworkSet.List>(
                "projectcalico.org", "v3", "globalnetworksets",
                labelSelector: ManagedLabels.SelectorFor(entity),
                cancellationToken: cancellationToken);
            return result.Items.FirstOrDefault();
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            // CRD not installed
            return null;
        }
    }

    private static string NameFor(V1FqdnNetworkPolicyEntity entity) => $"fqdn-{entity.Namespace()}-{entity.Name()}";
}
