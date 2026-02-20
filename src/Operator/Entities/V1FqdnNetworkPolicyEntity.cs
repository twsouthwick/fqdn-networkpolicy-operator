using System.Text.Json.Serialization;
using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace Swick.FqdnNetworkPolicyOperator.Entities;

[KubernetesEntity(Group = Constants.ApiGroup, ApiVersion = Constants.ApiVersion, Kind = Constants.Kind, PluralName = "fqdnnetworkpolicies")]
public partial class V1FqdnNetworkPolicyEntity : CustomKubernetesEntity<V1FqdnNetworkPolicyEntity.EntitySpec, V1FqdnNetworkPolicyEntity.EntityStatus>
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

        public ExternalProviderRef? ExternalProvider { get; set; }
    }

    public class ExternalProviderRef
    {
        [Required]
        public string ServiceName { get; set; } = null!;

        public string? DisplayName { get; set; }

        public int Port { get; set; } = 7942;

        public string Path { get; set; } = "/fqdnList";
    }

    public class EntityStatus
    {
        [AdditionalPrinterColumn]
        public bool Ready { get; set; }

        [AdditionalPrinterColumn]
        [JsonPropertyName("ipCount")]
        public int IPCount { get; set; }

        [AdditionalPrinterColumn]
        public int DomainCount { get; set; }

        [AdditionalPrinterColumn]
        public int WarningCount { get; set; }

        [AdditionalPrinterColumn]
        public DateTimeOffset? LastReconciled { get; set; }

        [AdditionalPrinterColumn]
        public DateTimeOffset? LastModified { get; set; }

        [AdditionalPrinterColumn]
        public string? Message { get; set; }
    }
}
