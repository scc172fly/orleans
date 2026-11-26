namespace Presence.Grains;

/// <summary>
/// 定义一个用于发送二进制更新的界面,而不知道具体的游戏 ID 。
/// 模拟游戏控制台在向云中发送数据时所做的一切。
/// </summary>
public interface IPresenceGrain : IGrainWithIntegerKey
{
    Task HeartbeatAsync(byte[] data);
}
