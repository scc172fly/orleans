namespace UnitTests.GrainInterfaces

{
    public interface IMultifacetWriter : IGrainWithIntegerKey
    {
        Task SetValue(int x);
        //事件 ValueUpdateEventHandler ValueReadEvent;
        //事件 ValueUpdateEventHandler CommonEvent;
    }
}
