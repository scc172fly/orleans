using Orleans.Concurrency;
using Presence.Grains.Models;

namespace Presence.Grains;

/// <summary>
/// 无国籍 grain 解码二进制的球体和路径,然后到合适的游戏 grains 基于上浮内容。
/// 模拟云服务如何从设备接收原始数据,并需要先处理后再将数据转发给实际计算。
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
