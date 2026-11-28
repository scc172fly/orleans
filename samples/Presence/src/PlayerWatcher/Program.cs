using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Presence.Grains;
using Presence.PlayerWatcher;

Console.Title = "PlayerWatcher";

await Host.CreateDefaultBuilder()
    .UseOrleansClient(builder => builder.UseLocalhostClustering())
    .ConfigureServices(services =>
    {
        // 添加常规服务。
        services.AddTransient<IGameObserver, LoggerGameObserver>();

        // 此托管服务运行示例逻辑。
        services.AddSingleton<IHostedService, PlayerWatcherHostedService>();
    })
    .RunConsoleAsync();
