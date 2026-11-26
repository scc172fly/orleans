namespace AdventureGrainInterfaces;

/// <summary>
/// A室是游戏中的任何地点,包括户外和
/// 可以说这些空间更被形容为潮湿、寒冷和洞穴。
/// </summary>
public interface IRoomGrain : IGrainWithIntegerKey
{
    // 房间有文字描述
    Task<string> Description(PlayerInfo whoisAsking);

    Task SetInfo(RoomInfo info);

    Task<IRoomGrain?> ExitTo(string direction);

    // 玩家可以进入或离开一个房间
    Task Enter(PlayerInfo player);

    Task Exit(PlayerInfo player);

    // 怪物可以进入或离开房间
    Task Enter(MonsterInfo monster);

    Task Exit(MonsterInfo monster);

    // 东西可以扔下或从房间里拿
    Task Drop(Thing thing);

    Task Take(Thing thing);

    Task<Thing?> FindThing(string name);

    // 如果你有正确的武器 玩家和怪物就会被杀
    Task<PlayerInfo?> FindPlayer(string name);

    Task<MonsterInfo?> FindMonster(string name);
}
