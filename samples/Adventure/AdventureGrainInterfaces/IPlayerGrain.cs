namespace AdventureGrainInterfaces;

/// <summary>
/// 玩家是,嗯,真的没有其他好名字...
/// </summary>
public interface IPlayerGrain : IGrainWithGuidKey
{
    // 玩家有名字
    Task<string?> Name();

    Task SetName(string name);

    // 每个玩家都在一个房间
    Task SetRoomGrain(IRoomGrain room);

    Task<IRoomGrain> RoomGrain();

    // 直到死亡降临
    Task Die();

    // 玩家通过调用游戏命令来轮到他
    Task<string?> Play(string command);
}
