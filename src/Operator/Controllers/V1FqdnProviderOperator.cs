using k8s.Models;
using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.KubernetesClient;
using Swick.FqdnNetworkPolicyOperator.Entities;

namespace Swick.FqdnNetworkPolicyOperator.Controllers;

[EntityRbac(typeof(V1Secret), Verbs = RbacVerb.Get | RbacVerb.Create | RbacVerb.Delete)]
[EntityRbac(typeof(V1FqdnProviderEntity), Verbs = RbacVerb.All)]
public class V1FqdnProviderOperator(IKubernetesClient client, ILogger<V1FqdnProviderOperator> logger)
    : IEntityController<V1FqdnProviderEntity>
{
    public Task<ReconciliationResult<V1FqdnProviderEntity>> DeletedAsync(V1FqdnProviderEntity entity, CancellationToken cancellationToken)
    {
        return Task.FromResult(ReconciliationResult<V1FqdnProviderEntity>.Success(entity));
    }

    public Task<ReconciliationResult<V1FqdnProviderEntity>> ReconcileAsync(V1FqdnProviderEntity entity, CancellationToken cancellationToken)
    {
        return Task.FromResult(ReconciliationResult<V1FqdnProviderEntity>.Success(entity));
    }
}