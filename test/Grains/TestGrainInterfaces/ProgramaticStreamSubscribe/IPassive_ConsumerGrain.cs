namespace UnitTests.GrainInterfaces
{
    /// <summary>
    /// 被动响应以编程方式
    /// 代表其建立的订阅的消费者 Grain
    /// </summary>
    public interface IPassive_ConsumerGrain : IGrainWithGuidKey
    {
        Task StopConsuming();
        Task<int> GetCountOfOnAddFuncCalled();
        Task<int> GetNumberConsumed();
    }

    //消费者 Grain 的标记接口，该 Grain 会取消 StreamSubscriptionManager 添加的任何订阅
    public interface IJerk_ConsumerGrain : IGrainWithGuidKey
    {
    }

    public interface IImplicitSubscribeGrain : IPassive_ConsumerGrain
    {
    }

    public interface ITypedProducerGrain : IGrainWithGuidKey
    {
        Task BecomeProducer(Guid streamId, string streamNamespace, string providerToUse);

        Task StartPeriodicProducing(TimeSpan? firePeriod = null);

        Task StopPeriodicProducing();

        Task<int> GetNumberProduced();

        Task ClearNumberProduced();
        Task Produce();
    }

    public interface ITypedProducerGrainProducingInt : ITypedProducerGrain
    { }

    public interface ITypedProducerGrainProducingApple : ITypedProducerGrain
    { }

    public interface IFruit
    {
        int GetNumber();
    }
}
