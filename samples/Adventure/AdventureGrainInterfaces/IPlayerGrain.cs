namespace AdventureGrainInterfaces;

/// <summary>
/// 玩家就是……玩家，实在找不到更好的名称了。
/// </summary>
public interface IPlayerGrain : IGrainWithGuidKey
{
    // 玩家有名字。
    Task<string?> Name();

    Task SetName(string name);

    // 每个玩家恰好位于一个房间中。
    Task SetRoomGrain(IRoomGrain room);

    Task<IRoomGrain> RoomGrain();

    // 直到死亡找上门为止。
    Task Die();

    // 玩家通过调用 Play 并传入命令来执行自己的回合。
    Task<string?> Play(string command);
}
