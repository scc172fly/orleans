namespace UnitTests.GrainInterfaces
{
    /// <summary>
    /// 消费者 grain 对代表用户
    /// 使用程序订阅 
    /// </summary>
    public interface IPassive_ConsumerGrain : IGrainWithGuidKey
    {
        Task StopConsuming();
        Task<int> GetCountOfOnAddFuncCalled();
        Task<int> GetNumberConsumed();
    }

    //消费者 grain 将取消订阅任何用户添加的 StreamSubscriptionManager
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
