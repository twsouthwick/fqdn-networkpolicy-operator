using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

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
        public string[]? Domains { get; set; }

        public V1NetworkPolicyPort[] Ports { get; set; } = [];

        public Provider? Provider { get; set; }
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
        [AdditionalPrinterColumn]
        public bool Ready { get; set; }

        [AdditionalPrinterColumn]
        public int IpCount { get; set; }

        [AdditionalPrinterColumn]
        public int DomainCount { get; set; }

        [AdditionalPrinterColumn]
        public DateTimeOffset? LastReconciled { get; set; }

        [AdditionalPrinterColumn]
        public DateTimeOffset? LastModified { get; set; }

        [AdditionalPrinterColumn]
        public string? Message { get; set; }
    }
}
