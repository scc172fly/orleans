namespace UnitTests.GrainInterfaces
{
    /// <summary>
    /// 仅统计所消费事件数量的流消费者 Grain
    /// </summary>
    public interface IConsumerEventCountingGrain : IGrainWithGuidKey
    {
        Task BecomeConsumer(Guid streamId, string providerToUse);

        Task StopConsuming();

        Task<int> GetNumberConsumed(CancellationToken cancellationToken = default);
    }
}
