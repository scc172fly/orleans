using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Presence.LoadGenerator;

Console.Title = nameof(Presence.LoadGenerator);

await new HostBuilder()
    .UseOrleansClient(builder => builder.UseLocalhostClustering())
    .ConfigureServices(services =>
        // 此托管服务使用可用的集群客户端生成负载。
        services.AddSingleton<IHostedService, LoadGeneratorHostedService>())
    .ConfigureLogging(builder => builder.AddConsole())
    .RunConsoleAsync();
