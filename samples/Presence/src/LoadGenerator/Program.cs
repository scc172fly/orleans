using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Presence.LoadGenerator;

Console.Title = nameof(Presence.LoadGenerator);

await new HostBuilder()
    .UseOrleansClient(builder => builder.UseLocalhostClustering())
    .ConfigureServices(services =>
        // 使用现有集群客户端进行负载生成
        services.AddSingleton<IHostedService, LoadGeneratorHostedService>())
    .ConfigureLogging(builder => builder.AddConsole())
    .RunConsoleAsync();
