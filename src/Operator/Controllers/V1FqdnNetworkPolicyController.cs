namespace Swick.FqdnNetworkPolicyOperator.Controllers;

[EntityRbac(typeof(V1NetworkPolicy), Verbs = RbacVerb.Get | RbacVerb.Create | RbacVerb.Update)]
[EntityRbac(typeof(V1FqdnNetworkPolicyEntity), Verbs = RbacVerb.Get | RbacVerb.Update)]
[EntityRbac(typeof(V3GlobalNetworkSet), Verbs = RbacVerb.Get | RbacVerb.Create | RbacVerb.Update | RbacVerb.Delete)]
public class V1FqdnNetworkPolicyController(EgressRuleResolver egressResolver, GlobalNetworkSetManager gnsManager, IKubernetesClient client, ILogger<V1FqdnNetworkPolicyController> logger)
    : IEntityController<V1FqdnNetworkPolicyEntity>
{
    public async Task<ReconciliationResult<V1FqdnNetworkPolicyEntity>> DeletedAsync(V1FqdnNetworkPolicyEntity entity, CancellationToken cancellationToken)
    {
        await gnsManager.DeleteAsync(entity, cancellationToken);
        return ReconciliationResult<V1FqdnNetworkPolicyEntity>.Success(entity);
    }

    public async Task<ReconciliationResult<V1FqdnNetworkPolicyEntity>> ReconcileAsync(V1FqdnNetworkPolicyEntity entity, CancellationToken cancellationToken)
    {
        try
        {
            var warnings = new List<string>();
            var egressRules = await egressResolver.ResolveAsync(entity, warnings, cancellationToken).ToListAsync(cancellationToken);

            var networkPolicyChanged = await ApplyNetworkPolicyAsync(entity, egressRules, cancellationToken);
            var gnsChanged = await gnsManager.ApplyAsync(entity, egressRules, cancellationToken);
            var changed = networkPolicyChanged || gnsChanged;

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
        var policyName = entity.Name();

        var spec = DeepCopySpec(entity);

        spec.Egress = spec.Egress is null
            ? egressRules
            : [.. spec.Egress, .. egressRules];

        var policy = new V1NetworkPolicy
        {
            Metadata = new V1ObjectMeta
            {
                Name = policyName,
                NamespaceProperty = entity.Namespace(),
                Labels = ManagedLabels.For(entity),
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

        static V1NetworkPolicySpec DeepCopySpec(V1FqdnNetworkPolicyEntity entity)
            => KubernetesJsonSerializer.Deserialize<V1NetworkPolicySpec>(KubernetesJsonSerializer.Serialize(entity.Spec.Policy));
    }
}