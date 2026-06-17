using DnsClient;
using KubeOps.Operator;
using Swick.FqdnNetworkPolicyOperator;
using Swick.FqdnNetworkPolicyOperator.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Trace);

builder.Services
    .AddSingleton<ILookupClient>(_ => new LookupClient(new LookupClientOptions
    {
        UseCache = true,
        MinimumCacheTimeout = TimeSpan.FromSeconds(30),
        CacheFailedResults = true,
        FailedResultsCacheDuration = TimeSpan.FromSeconds(30),
    }))
    .AddHttpClient()
    .AddTransient<EgressRuleResolver>()
    .AddTransient<GlobalNetworkSetManager>()
    .AddKubernetesOperator(settings =>
    {
        settings.AutoAttachFinalizers = false;
        settings.AutoDetachFinalizers = false;
    })
    .RegisterComponents();

using var host = builder.Build();
await host.RunAsync();
