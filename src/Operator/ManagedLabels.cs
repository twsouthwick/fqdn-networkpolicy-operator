namespace Swick.FqdnNetworkPolicyOperator;

internal static class ManagedLabels
{
    public static Dictionary<string, string> For(V1FqdnNetworkPolicyEntity entity) => new()
    {
        [$"{Constants.ApiGroup}/managed-by"] = "fqdn-networkpolicy-operator",
        [$"{Constants.ApiGroup}/name"] = entity.Name(),
        [$"{Constants.ApiGroup}/namespace"] = entity.Namespace(),
    };

    public static string SelectorFor(V1FqdnNetworkPolicyEntity entity) =>
        $"{Constants.ApiGroup}/managed-by=fqdn-networkpolicy-operator,{Constants.ApiGroup}/name={entity.Name()},{Constants.ApiGroup}/namespace={entity.Namespace()}";
}
