using System.Collections.Immutable;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Presence.Grains;
using Presence.Grains.Models;

namespace Presence.LoadGenerator;

public sealed class LoadGeneratorHostedService : BackgroundService
{
    private readonly ILogger<LoadGeneratorHostedService> _logger;
    private readonly IClusterClient _client;

    public LoadGeneratorHostedService(ILogger<LoadGeneratorHostedService> logger, IClusterClient client)
    {
        _logger = logger;
        _client = client;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 要模拟的游戏数量。
        var nGames = 10;

        // 每场游戏的玩家数量。
        var nPlayersPerGame = 4;

        // 发送更新的间隔。
        var sendInterval = TimeSpan.FromSeconds(2);

        // 要发送的更新次数。
        var nIterations = 100;

        // 为每场游戏预先创建基础心跳数据对象。
        // 每次发送之前都会修改这些对象。
        var heartbeats = new HeartbeatData[nGames];
        for (var i = 0; i < nGames; i++)
        {
            heartbeats[i] = new HeartbeatData(
                Guid.NewGuid(),
                new GameStatus(
                    Enumerable.Range(0, nPlayersPerGame).Select(j => GetPlayerId(i * nPlayersPerGame + j)).ToImmutableHashSet(),
                    string.Empty));
        }

        var iteration = 0;

        // PresenceGrain 是 StatelessWorker，因此使用单个 Grain ID 即可实现自动扩展。
        var presence = _client.GetGrain<IPresenceGrain>(0);
        var promises = new Task[nGames];

        while (!stoppingToken.IsCancellationRequested && ++iteration < nIterations)
        {
            _logger.LogInformation("Sending heartbeat series #{@Iteration}", iteration);

            try
            {
                for (var i = 0; i < nGames; i++)
                {
                    // 模拟有意义的游戏分数。
                    heartbeats[i] = heartbeats[i].WithNewScore($"{iteration}:{(iteration > 5 ? iteration - 5 : 0)}");

                    // 将 HeartbeatData 对象序列化为 byte[]，只是为了模拟现实场景中数据
                    // 以二进制数据块传入，需要先经过初步处理才能路由到正确目的地的情况。
                    // 由于已经知道游戏 ID，本可以将 HeartbeatData 对象直接发送给游戏 Grain。
                    // 为了模拟该场景，假设我们不知道游戏 ID。
                    promises[i] = presence.HeartbeatAsync(HeartbeatDataDotNetSerializer.Serialize(heartbeats[i]));
                }

                // 等待所有调用完成。
                await Task.WhenAll(promises);
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Error while sending hearbeats to Orleans cluster");
            }

            try
            {
                await Task.Delay(sendInterval, stoppingToken);
            }
            catch when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 为玩家 ID 生成 GUID。
    /// </summary>
    private static Guid GetPlayerId(int playerIndex)
    {
        // 为方便起见，以此 GUID 为基础为玩家生成一组预定义的后续 GUID。
        return Guid.ParseExact($"2349992C-860A-4EDA-9590-{playerIndex:D12}", "D");
    }
}
