using System;
using System.Threading;
using System.Threading.Tasks;


namespace Orleans.Runtime
{
    internal interface IMembershipService : ISystemTarget
    {
        /// <summary>
        /// 接收有关成员表变更的通知。
        /// </summary>
        /// <param name="snapshot">成员表的快照。</param>
        /// <returns></returns>
        [Alias("22A02D46")]
        Task MembershipChangeNotification(MembershipTableSnapshot snapshot, CancellationToken cancellationToken = default);

        /// <summary>
        /// 来自另一个 Silo 的 Ping 请求，用于探测接收方 Silo 的存活状态。
        /// </summary>
        /// <param name="pingNumber">Ping 消息的唯一序列号，便于测试和调试。</param>
        [Alias("39AB7071")]
        Task Ping(int pingNumber, CancellationToken cancellationToken = default);

        [Alias("0F85FAAF")]
        Task<IndirectProbeResponse> ProbeIndirectly(SiloAddress target, TimeSpan probeTimeout, int probeNumber, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 表示通过中间节点探测某个节点的结果。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public readonly struct IndirectProbeResponse
    {
        /// <summary>
        /// 中间节点的健康评分。
        /// </summary>
        [Id(0)]
        public int IntermediaryHealthScore { get; init; }

        /// <summary>
        /// <see langword="true"/> 表示探测成功，否则为 <see langword="false"/>。
        /// </summary>
        [Id(1)]
        public bool Succeeded { get; init; }

        /// <summary>
        /// 探测尝试的持续时间。
        /// </summary>
        [Id(2)]
        public TimeSpan ProbeResponseTime { get; init; }

        /// <summary>
        /// 探测未成功时的失败消息。
        /// </summary>
        [Id(3)]
        public string? FailureMessage { get; init; }

        /// <inheritdoc />
        public override string ToString() => $"IndirectProbeResponse {{ Succeeded: {Succeeded}, IntermediaryHealthScore: {IntermediaryHealthScore}, ProbeResponseTime: {ProbeResponseTime}, FailureMessage: {FailureMessage} }}";
    }
}
