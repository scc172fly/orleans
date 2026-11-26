namespace AdventureGrainInterfaces;

public interface IMonsterGrain : IGrainWithIntegerKey
{
    // 连怪物都有名字
    Task<string?> Name();

    Task SetInfo(MonsterInfo info);

    // 怪物就在一个房间
    Task SetRoomGrain(IRoomGrain room);

    Task<IRoomGrain> RoomGrain();

    Task<string> Kill(IRoomGrain room);
}
