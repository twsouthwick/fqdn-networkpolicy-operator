var builder = Host.CreateApplicationBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Trace);

builder.Services
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
