namespace UnitTests.GrainInterfaces
{
    /// <summary>
    /// 仅计算消费量的流消费 grain
    /// </summary>
    public interface IConsumerEventCountingGrain : IGrainWithGuidKey
    {
        Task BecomeConsumer(Guid streamId, string providerToUse);

        Task StopConsuming();

        Task<int> GetNumberConsumed(CancellationToken cancellationToken = default);
    }
}
