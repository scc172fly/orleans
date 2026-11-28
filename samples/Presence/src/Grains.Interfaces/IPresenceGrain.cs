namespace Presence.Grains;

/// <summary>
/// 定义无需知道具体游戏 ID 即可发送二进制更新的接口。
/// 模拟游戏主机向云端发送数据的方式。
/// </summary>
public interface IPresenceGrain : IGrainWithIntegerKey
{
    Task HeartbeatAsync(byte[] data);
}
