
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Finalizer;
using Swick.FqdnNetworkPolicyOperator.Entities;

namespace Swick.FqdnNetworkPolicyOperator.Finalizers;

public class V1FqdnProviderEntityFinalizer : IEntityFinalizer<V1FqdnProviderEntity>
{
    public Task<ReconciliationResult<V1FqdnProviderEntity>> FinalizeAsync(V1FqdnProviderEntity entity, CancellationToken cancellationToken)
    {
        return Task.FromResult(ReconciliationResult<V1FqdnProviderEntity>.Success(entity));
    }
}
