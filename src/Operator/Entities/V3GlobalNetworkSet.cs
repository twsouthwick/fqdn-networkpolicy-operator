using System.Text.Json.Serialization;
using k8s;
using k8s.Models;

namespace Swick.FqdnNetworkPolicyOperator.Entities;

[KubernetesEntity(Group = "projectcalico.org", ApiVersion = "v3", Kind = "GlobalNetworkSet", PluralName = "globalnetworksets")]
public class V3GlobalNetworkSet : IKubernetesObject<V1ObjectMeta>
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = "projectcalico.org/v3";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "GlobalNetworkSet";

    [JsonPropertyName("metadata")]
    public V1ObjectMeta Metadata { get; set; } = new();

    [JsonPropertyName("spec")]
    public GlobalNetworkSetSpec Spec { get; set; } = new();

    public class GlobalNetworkSetSpec
    {
        [JsonPropertyName("nets")]
        public List<string> Nets { get; set; } = [];
    }

    public class List
    {
        [JsonPropertyName("items")]
        public V3GlobalNetworkSet[] Items { get; set; } = [];
    }
}
