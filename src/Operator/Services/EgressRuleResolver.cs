using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using DnsClient;
using DnsClient.Protocol;
using k8s.Models;
using Swick.FqdnNetworkPolicyOperator.Entities;

namespace Swick.FqdnNetworkPolicyOperator;

public class EgressRuleResolver(HttpClient httpClient, ILookupClient dnsClient, ILogger<EgressRuleResolver> logger)
{
    public async IAsyncEnumerable<V1NetworkPolicyEgressRule> ResolveAsync(V1FqdnNetworkPolicyEntity entity, List<string> warnings, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var egress in entity.Spec.Egress)
        {
            if (egress.Domains is not null)
            {
                var peers = await ResolveIpNetworksAsync(egress.Domains, warnings, cancellationToken)
                    .Order(IPNetworkComparer.Instance)
                    .Select(network => new V1NetworkPolicyPeer { IpBlock = new V1IPBlock { Cidr = network.ToString() } })
                    .ToListAsync(cancellationToken);

                yield return new V1NetworkPolicyEgressRule
                {
                    To = peers,
                    Ports = egress.Ports
                };
            }

            if (egress.ExternalProvider is not null)
            {
                await foreach (var rule in ResolveFromProviderAsync(entity, egress.ExternalProvider, warnings, cancellationToken))
                {
                    yield return rule;
                }
            }
        }
    }

    private async IAsyncEnumerable<V1NetworkPolicyEgressRule> ResolveFromProviderAsync(V1FqdnNetworkPolicyEntity entity, V1FqdnNetworkPolicyEntity.ExternalProviderRef provider, List<string> warnings, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching addresses from external provider {ServiceName}:{Port} for {Name}", provider.ServiceName, provider.Port, entity.Name());

        var envelope = await httpClient.GetFromJsonAsync<ProviderEnvelope>($"http://{provider.ServiceName}.{entity.Namespace()}:{provider.Port}{provider.Path}", cancellationToken);

        if (envelope is null)
        {
            var msg = $"Null response from external provider service '{provider.ServiceName}'";
            logger.LogWarning("Received null response from external provider {ServiceName} for {Name}", provider.ServiceName, entity.Name());
            warnings.Add(msg);
            yield break;
        }

        foreach (var response in envelope.Egress)
        {
            var peers = await ResolveIpNetworksAsync(response.Addresses, warnings, cancellationToken)
                .Order(IPNetworkComparer.Instance)
                .Select(network => new V1NetworkPolicyPeer { IpBlock = new V1IPBlock { Cidr = network.ToString() } })
                .ToListAsync(cancellationToken);

            yield return new V1NetworkPolicyEgressRule
            {
                To = peers,
                Ports = response.Ports
            };
        }
    }

    private async IAsyncEnumerable<IPNetwork> ResolveIpNetworksAsync(IEnumerable<string> ipOrAddresses, List<string> warnings, [EnumeratorCancellation] CancellationToken token)
    {
        foreach (var ipOrAddress in ipOrAddresses)
        {
            if (IPAddress.TryParse(ipOrAddress, out var address))
            {
                yield return new IPNetwork(address, 32);
            }
            else if (IPNetwork.TryParse(ipOrAddress, out var network))
            {
                yield return network;
            }
            else
            {
                IPAddress[] addresses = [];

                try
                {
                    var result = await dnsClient.QueryAsync(ipOrAddress, QueryType.A, cancellationToken: token);
                    var result6 = await dnsClient.QueryAsync(ipOrAddress, QueryType.AAAA, cancellationToken: token);
                    
                    addresses = result.Answers.ARecords().Select(r => r.Address)
                        .Concat(result6.Answers.AaaaRecords().Select(r => r.Address))
                        .ToArray();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (DnsResponseException ex)
                {
                    warnings.Add($"Could not resolve '{ipOrAddress}'");
                    logger.LogWarning(ex, "Could not resolve '{Fqdn}' from provider response", ipOrAddress);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to resolve '{ipOrAddress}': {ex.Message}");
                    logger.LogWarning(ex, "Failed to resolve FQDN '{Fqdn}' from provider response", ipOrAddress);
                }

                foreach (var a in addresses)
                {
                    yield return new IPNetwork(a, 32);
                }
            }
        }
    }

    private sealed class ProviderEnvelope
    {
        [JsonPropertyName("egress")]
        public ProviderEgressRule[] Egress { get; set; } = [];
    }

    private sealed class ProviderEgressRule
    {
        [JsonPropertyName("addresses")]
        public string[] Addresses { get; set; } = [];

        [JsonPropertyName("ports")]
        public V1NetworkPolicyPort[] Ports { get; set; } = [];
    }
}
