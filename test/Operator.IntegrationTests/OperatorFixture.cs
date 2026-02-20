using k8s;
using k8s.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using KubeOps.Operator;
using Swick.FqdnNetworkPolicyOperator.Entities;

namespace Swick.FqdnNetworkPolicyOperator.IntegrationTests;

/// <summary>
/// Fixture that runs the operator in-process for integration testing.
/// </summary>
public class OperatorFixture : IAsyncDisposable
{
    private IHost? _host;
    private CancellationTokenSource? _cts;

    public IKubernetes Client { get; }
    public string TestNamespace { get; }

    public OperatorFixture(IKubernetes client, string testNamespace)
    {
        Client = client;
        TestNamespace = testNamespace;
    }

    public async Task StartOperatorAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddConsole();

        builder.Services
            .AddHttpClient()
            .AddKubernetesOperator()
            .RegisterComponents();

        _host = builder.Build();

        // Set up a task to complete when the host has started
        var lifetime = _host.Services.GetRequiredService<IHostApplicationLifetime>();
        var startedTcs = new TaskCompletionSource();
        using var registration = lifetime.ApplicationStarted.Register(() => startedTcs.SetResult());

        // Start the operator in the background
        _ = _host.RunAsync(_cts.Token);

        // Wait for operator to be fully started
        await startedTcs.Task.WaitAsync(cancellationToken);
    }

    public async Task<T> WaitForConditionAsync<T>(
        Func<Task<T?>> getResource,
        Func<T?, bool> condition,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) where T : class
    {
        timeout ??= TimeSpan.FromSeconds(30);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resource = await getResource();
            if (condition(resource))
            {
                return resource!;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException($"Condition not met within {timeout}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }

        if (_host != null)
        {
            _host.Dispose();
        }
    }
}
