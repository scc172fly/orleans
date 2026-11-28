namespace UnitTests.GrainInterfaces
{
    /// <summary>
    /// 每次接到指令（参见 SendEvent）仅发送一个事件并统计已发送事件数量的流生产者 Grain
    /// </summary>
    public interface IProducerEventCountingGrain : IGrainWithGuidKey
    {
        Task BecomeProducer(Guid streamId, string providerToUse);

        /// <summary>
        /// 发送单个事件，并在成功完成后更新已生产的事件数量。
        /// </summary>
        /// <returns></returns>
        Task SendEvent();

        Task<int> GetNumberProduced(CancellationToken cancellationToken = default);
    }
}
