using Orleans.Concurrency;
using Presence.Grains.Models;

namespace Presence.Grains;

/// <summary>
/// 无状态 Grain，解码二进制数据块，并根据其内容将数据路由到相应的游戏 Grain。
/// 模拟云服务从设备接收原始数据、在转发以进行实际计算之前需要预处理数据的过程。
/// </summary>
[StatelessWorker]
public class PresenceGrain : Grain, IPresenceGrain
{
    public Task HeartbeatAsync(byte[] data)
    {
        var heartbeatData = HeartbeatDataDotNetSerializer.Deserialize(data);
        var game = GrainFactory.GetGrain<IGameGrain>(heartbeatData.GameKey);
        return game.UpdateGameStatusAsync(heartbeatData.Status);
    }
}
