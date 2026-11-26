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
        // 要模拟的游戏数
        var nGames = 10;

        // 每一游戏中玩家数
        var nPlayersPerGame = 4;

        // 发送更新的间隔
        var sendInterval = TimeSpan.FromSeconds(2);

        // 要发送的更新数量
        var nIterations = 100;

        // 为每个游戏预设基心跳数据对象 。
        // 我们每次发送之前都要修改
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

        // PresenceGrain 是 StatelessWorker, 所以我们使用一个单 grain ID 来自动缩放
        var presence = _client.GetGrain<IPresenceGrain>(0);
        var promises = new Task[nGames];

        while (!stoppingToken.IsCancellationRequested && ++iteration < nIterations)
        {
            _logger.LogInformation("Sending heartbeat series #{@Iteration}", iteration);

            try
            {
                for (var i = 0; i < nGames; i++)
                {
                    // 模拟有意义的游戏分数
                    heartbeats[i] = heartbeats[i].WithNewScore($"{iteration}:{(iteration > 5 ? iteration - 5 : 0)}");

                    // 我们将 HeartbeatData 对象与字节相序号,仅用于模拟数据输入的真实生活情景
                    // 是一个二进制的浮标,需要先进行初步处理后才能被送到适当的目的地。
                    // 我们可以直接将 HeartbeatData 对象发送到游戏 grain ,因为我们知道游戏的代号。
                    // 为了模拟我们假装没有
                    promises[i] = presence.HeartbeatAsync(HeartbeatDataDotNetSerializer.Serialize(heartbeats[i]));
                }

                // 等待所有电话都打完
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
    /// 生成玩家身份识别的图形界面D。
    /// </summary>
    private static Guid GetPlayerId(int playerIndex)
    {
        // 为了方便起见,我们为使用此选项作为基础的玩家制作一套预先定义的后续图形界面数据。
        return Guid.ParseExact($"2349992C-860A-4EDA-9590-{playerIndex:D12}", "D");
    }
}
