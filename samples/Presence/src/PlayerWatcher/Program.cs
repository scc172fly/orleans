using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Presence.Grains;
using Presence.PlayerWatcher;

Console.Title = "PlayerWatcher";

await Host.CreateDefaultBuilder()
    .UseOrleansClient(builder => builder.UseLocalhostClustering())
    .ConfigureServices(services =>
    {
        // 添加经常服务
        services.AddTransient<IGameObserver, LoggerGameObserver>();

        // 服务主机运行的逻辑样本
        services.AddSingleton<IHostedService, PlayerWatcherHostedService>();
    })
    .RunConsoleAsync();
