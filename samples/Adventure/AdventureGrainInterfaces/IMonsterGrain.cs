namespace AdventureGrainInterfaces;

public interface IMonsterGrain : IGrainWithIntegerKey
{
    // 即使是怪物也有名字。
    Task<string?> Name();

    Task SetInfo(MonsterInfo info);

    // 每个怪物恰好位于一个房间中。
    Task SetRoomGrain(IRoomGrain room);

    Task<IRoomGrain> RoomGrain();

    Task<string> Kill(IRoomGrain room);
}
