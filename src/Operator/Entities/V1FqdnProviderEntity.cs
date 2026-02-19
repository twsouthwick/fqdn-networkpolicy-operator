using k8s.Models;
using KubeOps.Abstractions.Entities;
using System.ComponentModel.DataAnnotations;

namespace Swick.FqdnNetworkPolicyOperator.Entities;

[KubernetesEntity(Group = Constants.Namespace, ApiVersion = Constants.ApiVersion, Kind = Constants.Provider, PluralName = "providers")]
public partial class V1FqdnProviderEntity : CustomKubernetesEntity<V1FqdnProviderEntity.EntitySpec, V1FqdnProviderEntity.EntityStatus>
{
    public class EntitySpec
    {
        [Required]
        public string ServiceName { get; set; } = null!;

        public int Port { get; set; } = 7942;

    }

    public class EntityStatus
    {
        public int FqdnCount { get; set; }

        public int IpCount { get; set; }

        public DateTimeOffset? LastUpdated { get; set; }
    }
}
