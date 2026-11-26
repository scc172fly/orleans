using Orleans.Concurrency;

namespace UnitTests.GrainInterfaces
{
    //- - - - - - - - - - - 葛赖因接口
    public interface IStreaming_ProducerGrain : IGrainWithGuidKey
    {
        Task BecomeProducer(Guid streamId, string providerToUse, string streamNamespace);
        Task StopBeingProducer();
        Task ProduceSequentialSeries(int count);
        Task ProduceParallelSeries(int count);
        Task ProducePeriodicSeries(int count);
        Task<int> GetExpectedItemsProduced();
        Task<int> GetItemsProduced();
        Task AddNewConsumerGrain(Guid consumerGrainId);
        Task<int> GetProducerCount();
        Task DeactivateProducerOnIdle();

        [AlwaysInterleave]
        Task VerifyFinished();
    }
}
