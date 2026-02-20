using k8s.Models;
using KubeOps.Abstractions.Entities;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Swick.FqdnNetworkPolicyOperator.Entities;

[KubernetesEntity(Group = Constants.Namespace, ApiVersion = Constants.ApiVersion, Kind = Constants.Provider, PluralName = "providers")]
public partial class V1FqdnProviderEntity : CustomKubernetesEntity<V1FqdnProviderEntity.EntitySpec, V1FqdnProviderEntity.EntityStatus>
{
    public class EntitySpec
    {
        public EgressRule[] Egress { get; set; } = null!;

        [Required]
        public k8s.Models.V1NetworkPolicySpec Policy { get; set; } = null!;
    }

    public class EgressRule
    {
        public EgressRuleItem[] To { get; set; } = [];

        public V1NetworkPolicyPort[] Ports { get; set; } = [];
    }

    public class EgressRuleItem
    {
        public string[]? Domains { get; set; }

        public Provider[]? Providers { get; set; }
    }

    public class Provider
    {
        [Required]
        public string ServiceName { get; set; } = null!;

        public string? Name { get; set; }

        public int Port { get; set; } = 7942;

        public string Path { get; set; } = "/fqdnlist";
    }

    public class EntityStatus
    {
        public int FqdnCount { get; set; }

        public int IpCount { get; set; }

        public DateTimeOffset? LastUpdated { get; set; }
    }
}
