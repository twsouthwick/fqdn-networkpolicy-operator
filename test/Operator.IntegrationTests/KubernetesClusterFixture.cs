using System.Diagnostics;
using k8s;
using Xunit;

namespace Operator.IntegrationTests;

/// <summary>
/// Fixture that ensures the Kubernetes cluster is ready and CRDs are applied.
/// This runs once per test collection.
/// </summary>
public class KubernetesClusterFixture : IAsyncLifetime
{
    public IKubernetes Client { get; private set; } = null!;
    public string TestNamespace { get; } = $"test-{Guid.NewGuid():N}"[..20];
    public bool IsClusterAvailable { get; private set; }

    public async ValueTask InitializeAsync()
    {
        // Check if a Kubernetes cluster is available
        IsClusterAvailable = await IsClusterAvailableAsync();

        if (!IsClusterAvailable)
        {
            return; // Tests will skip via Assert.SkipUnless
        }

        // Apply CRDs
        await ApplyCrdsAsync();

        // Create Kubernetes client
        var config = KubernetesClientConfiguration.BuildDefaultConfig();
        Client = new Kubernetes(config);

        // Create test namespace
        await CreateTestNamespaceAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (!IsClusterAvailable || Client is null)
        {
            return;
        }

        // Clean up test namespace
        try
        {
            await Client.CoreV1.DeleteNamespaceAsync(TestNamespace);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private static async Task<bool> IsClusterAvailableAsync()
    {
        // Check if kubectl can connect to a cluster
        var result = await RunCommandAsync("kubectl", "cluster-info");
        return result.ExitCode == 0;
    }

    private static async Task ApplyCrdsAsync()
    {
        // Find the workspace root by looking for the solution file
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "fqdn-networkpolicy-operator.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            throw new InvalidOperationException("Could not find workspace root (fqdn-networkpolicy-operator.slnx)");
        }

        var crdPath = Path.Combine(dir.FullName, "artifacts", "k8s", 
            "providers_com_github_twsouthwick_fqdnnetpol.yaml");

        if (!File.Exists(crdPath))
        {
            throw new FileNotFoundException($"CRD file not found at: {crdPath}");
        }

        var result = await RunCommandAsync("kubectl", $"apply -f \"{crdPath}\"");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to apply CRD: {result.Output}");
        }

        // Wait for CRD to be established
        await Task.Delay(1000);
    }

    private async Task CreateTestNamespaceAsync()
    {
        var ns = new k8s.Models.V1Namespace
        {
            Metadata = new k8s.Models.V1ObjectMeta
            {
                Name = TestNamespace
            }
        };

        await Client.CoreV1.CreateNamespaceAsync(ns);
    }

    private static async Task<(int ExitCode, string Output)> RunCommandAsync(string command, string args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, string.IsNullOrEmpty(error) ? output : $"{output}\n{error}");
    }
}

[CollectionDefinition("Kubernetes")]
public class KubernetesCollection : ICollectionFixture<KubernetesClusterFixture>
{
}
