using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Orleans.Configuration;

namespace Client;

internal class ExampleExternalProgram
{
    internal static async Task RunWatcherAsync(string[] args)
    {
        // <program>
        try
        {
            using IHost host = Host.CreateDefaultBuilder(args)
                .UseOrleansClient((context, client) =>
                {
                    client.Configure<ClusterOptions>(options =>
                    {
                        options.ClusterId = "my-first-cluster";
                        options.ServiceId = "MyOrleansService";
                    })
                    .UseAzureStorageClustering(
                        options => options.TableServiceClient = new TableServiceClient(
                            context.Configuration["ORLEANS_AZURE_STORAGE_CONNECTION_STRING"]));
                })
                .UseConsoleLifetime()
                .Build();

            await host.StartAsync();

            IGrainFactory client = host.Services.GetRequiredService<IGrainFactory>();

            // 硬编码的玩家 ID。
            Guid playerId = new("{2349992C-860A-4EDA-9590-000000000006}");
            IPlayerGrain player = client.GetGrain<IPlayerGrain>(playerId);
            IGameGrain? game = null;
            while (game is null)
            {
                Console.WriteLine(
                    $"Getting current game for player {playerId}...");

                try
                {
                    game = await player.GetCurrentGame();
                    if (game is null) // 等待玩家加入游戏。
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(5_000));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception: {ex.GetBaseException()}");
                }
            }

            Console.WriteLine(
                $"Subscribing to updates for game {game.GetPrimaryKey()}...");

            // 订阅更新。
            var watcher = new GameObserver();
            await game.ObserveGameUpdates(
                client.CreateObjectReference<IGameObserver>(watcher));

            Console.WriteLine(
                "Subscribed successfully. Press <Enter> to stop.");
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"Unexpected Error: {e.GetBaseException()}");
        }
        // </program>
    }
}

// <gameobserver>
/// <summary>
/// 实现观察者接口的观察者类。
/// 需要向此类的实例传入 Grain 引用，
/// 以订阅更新。【译注：此示例实际为观察者实例创建对象引用，再将其传给 Grain 的 ObserveGameUpdates。】
/// </summary>
class GameObserver : IGameObserver
{
    public void UpdateGameScore(string score)
    {
        Console.WriteLine($"New game score: {score}");
    }
}
// </gameobserver>

// <igameobserver>
public interface IGameObserver : IGrainObserver
{
    void UpdateGameScore(string score);
}
// </igameobserver>
