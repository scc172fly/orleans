namespace UnitTests.GrainInterfaces

{
    public interface IMultifacetReader : IGrainWithIntegerKey
    {
        Task<int> GetValue();
        //事件 ValueUpdateEventHandler ValueUpdateEvent;
        //事件 ValueUpdateEventHandler CommonEvent;
    }
}
