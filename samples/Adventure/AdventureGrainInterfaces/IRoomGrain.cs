namespace AdventureGrainInterfaces;

/// <summary>
/// 房间指游戏中的任意地点，包括室外区域和
/// 那些更适合描述为潮湿、寒冷洞穴的空间。
/// </summary>
public interface IRoomGrain : IGrainWithIntegerKey
{
    // 房间有文字描述。
    Task<string> Description(PlayerInfo whoisAsking);

    Task SetInfo(RoomInfo info);

    Task<IRoomGrain?> ExitTo(string direction);

    // 玩家可以进入或离开房间。
    Task Enter(PlayerInfo player);

    Task Exit(PlayerInfo player);

    // 怪物可以进入或离开房间。
    Task Enter(MonsterInfo monster);

    Task Exit(MonsterInfo monster);

    // 可以在房间中丢下或拾取物品。
    Task Drop(Thing thing);

    Task Take(Thing thing);

    Task<Thing?> FindThing(string name);

    // 如果有合适的武器，可以击杀玩家和怪物。
    Task<PlayerInfo?> FindPlayer(string name);

    Task<MonsterInfo?> FindMonster(string name);
}
