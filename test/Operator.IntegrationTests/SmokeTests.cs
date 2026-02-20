using k8s;
using k8s.Models;
using Swick.FqdnNetworkPolicyOperator.Entities;
using Xunit;

namespace Swick.FqdnNetworkPolicyOperator.IntegrationTests;

/// <summary>
/// End-to-end smoke tests: create an FqdnNetworkPolicy CR, run the operator in-process,
/// and verify that a NetworkPolicy is produced with the expected egress rules.
/// </summary>
[Collection("Kubernetes")]
public class SmokeTests(KubernetesClusterFixture cluster) : IAsyncLifetime
{
    private const string Group = "fqdnnetpol.swick.dev";
    private const string ApiVersion = "v1alpha1";
    private const string PluralName = "fqdnnetworkpolicies";

    private OperatorFixture? _operator;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(cluster.IsClusterAvailable, "No Kubernetes cluster available.");

        _operator = new OperatorFixture(cluster.Client, cluster.TestNamespace);
        await _operator.StartOperatorAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_operator is not null)
        {
            await _operator.DisposeAsync();
        }
    }

    [Fact]
    public async Task SingleRawIp_CreatesNetworkPolicyWithExpectedCidr()
    {
        var crName = "smoke-raw-ip";
        var expectedCidr = "1.2.3.4/32";
        var ct = TestContext.Current.CancellationToken;

        // Arrange: create an FqdnNetworkPolicy using a raw IP (no DNS lookup needed)
        var cr = new V1FqdnNetworkPolicyEntity
        {
            Metadata = new V1ObjectMeta
            {
                Name = crName,
                NamespaceProperty = cluster.TestNamespace,
            },
            Spec = new V1FqdnNetworkPolicyEntity.EntitySpec
            {
                Egress =
                [
                    new V1FqdnNetworkPolicyEntity.EgressRule
                    {
                        Domains = ["1.2.3.4"],
                        Ports =
                        [
                            new V1NetworkPolicyPort { Port = 443, Protocol = "TCP" }
                        ]
                    }
                ],
                Policy = new V1NetworkPolicySpec
                {
                    PodSelector = new V1LabelSelector(),
                    PolicyTypes = ["Egress"],
                },
            },
        };

        await cluster.Client.CustomObjects.CreateNamespacedCustomObjectAsync(
            cr, Group, ApiVersion, cluster.TestNamespace, PluralName,
            cancellationToken: ct);

        // Act: wait for the NetworkPolicy to appear
        var networkPolicy = await _operator!.WaitForConditionAsync(
            async () =>
            {
                try
                {
                    return await cluster.Client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(
                        crName, cluster.TestNamespace, cancellationToken: ct);
                }
                catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }
            },
            policy => policy is not null,
            timeout: TimeSpan.FromSeconds(30),
            cancellationToken: ct);

        // Assert: egress rule contains the expected CIDR
        Assert.NotNull(networkPolicy);
        Assert.NotNull(networkPolicy.Spec.Egress);
        var cidrs = networkPolicy.Spec.Egress
            .SelectMany(r => r.To ?? [])
            .Select(peer => peer.IpBlock?.Cidr)
            .ToList();

        Assert.Contains(expectedCidr, cidrs);

        // Assert: status is updated to Ready
        var updatedCr = await _operator.WaitForConditionAsync(
            () => cluster.Client.CustomObjects.GetNamespacedCustomObjectAsync<V1FqdnNetworkPolicyEntity>(
                Group, ApiVersion, cluster.TestNamespace, PluralName, crName,
                cancellationToken: ct),
            entity => entity?.Status.Ready == true,
            timeout: TimeSpan.FromSeconds(30),
            cancellationToken: ct);

        Assert.True(updatedCr.Status.Ready);
        Assert.Equal(1, updatedCr.Status.IPCount);
        Assert.Equal("Success", updatedCr.Status.Message);
    }

    [Fact]
    public async Task DeletedCr_RemovesNetworkPolicy()
    {
        var crName = "smoke-gc";
        var ct = TestContext.Current.CancellationToken;

        // Arrange: create and wait for NetworkPolicy to appear
        var cr = new V1FqdnNetworkPolicyEntity
        {
            Metadata = new V1ObjectMeta
            {
                Name = crName,
                NamespaceProperty = cluster.TestNamespace,
            },
            Spec = new V1FqdnNetworkPolicyEntity.EntitySpec
            {
                Egress =
                [
                    new V1FqdnNetworkPolicyEntity.EgressRule
                    {
                        Domains = ["10.0.0.1"],
                    }
                ],
                Policy = new V1NetworkPolicySpec
                {
                    PodSelector = new V1LabelSelector(),
                    PolicyTypes = ["Egress"],
                },
            },
        };

        await cluster.Client.CustomObjects.CreateNamespacedCustomObjectAsync(
            cr, Group, ApiVersion, cluster.TestNamespace, PluralName,
            cancellationToken: ct);

        // Wait for NetworkPolicy to be created
        await _operator!.WaitForConditionAsync(
            async () =>
            {
                try
                {
                    return await cluster.Client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(
                        crName, cluster.TestNamespace, cancellationToken: ct);
                }
                catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }
            },
            policy => policy is not null,
            timeout: TimeSpan.FromSeconds(30),
            cancellationToken: ct);

        // Act: delete the CR
        await cluster.Client.CustomObjects.DeleteNamespacedCustomObjectAsync(
            Group, ApiVersion, cluster.TestNamespace, PluralName, crName,
            cancellationToken: ct);

        // Assert: NetworkPolicy is garbage-collected via owner reference
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await cluster.Client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(
                    crName, cluster.TestNamespace, cancellationToken: ct);
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return; // gone — test passes
            }

            await Task.Delay(500, ct);
        }

        Assert.Fail($"NetworkPolicy '{crName}' was not garbage-collected within 30 seconds.");
    }
}
