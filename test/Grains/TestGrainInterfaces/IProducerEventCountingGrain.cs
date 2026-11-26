namespace UnitTests.GrainInterfaces
{
    /// <summary>
    /// 流制制制 grain ,它一次发送单一事件(当被通知时,请见 SendEvent)并跟踪所发送事件的数量
    /// </summary>
    public interface IProducerEventCountingGrain : IGrainWithGuidKey
    {
        Task BecomeProducer(Guid streamId, string providerToUse);

        /// <summary>
        /// 发送单一事件,并在成功完成后更新所制作活动的数量。
        /// </summary>
        /// <returns></returns>
        Task SendEvent();

        Task<int> GetNumberProduced(CancellationToken cancellationToken = default);
    }
}
