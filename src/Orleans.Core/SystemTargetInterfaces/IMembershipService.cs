using System;
using System.Threading;
using System.Threading.Tasks;


namespace Orleans.Runtime
{
    internal interface IMembershipService : ISystemTarget
    {
        /// <summary>
        /// 收到关于成员表变更的通知
        /// </summary>
        /// <param name="snapshot">成员名单</param>
        /// <returns></returns>
        [Alias("22A02D46")]
        Task MembershipChangeNotification(MembershipTableSnapshot snapshot, CancellationToken cancellationToken = default);

        /// <summary>
        /// 另一人提出的平方请求 silo 探测接收者的生命力 silo.
        /// </summary>
        /// <param name="pingNumber">用于拨号消息的独特序列编号,以便利测试和调试。</param>
        [Alias("39AB7071")]
        Task Ping(int pingNumber, CancellationToken cancellationToken = default);

        [Alias("0F85FAAF")]
        Task<IndirectProbeResponse> ProbeIndirectly(SiloAddress target, TimeSpan probeTimeout, int probeNumber, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 代表通过中间节点探查一个节点的结果。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public readonly struct IndirectProbeResponse
    {
        /// <summary>
        /// 中间节点的健康得分
        /// </summary>
        [Id(0)]
        public int IntermediaryHealthScore { get; init; }

        /// <summary>
        /// <see langword="true"/> 如果探测器成功;否则, <see langword="false"/>。
        /// </summary>
        [Id(1)]
        public bool Succeeded { get; init; }

        /// <summary>
        /// 探测尝试的持续时间
        /// </summary>
        [Id(2)]
        public TimeSpan ProbeResponseTime { get; init; }

        /// <summary>
        /// 如果探测器未能成功, 则失败信息 。
        /// </summary>
        [Id(3)]
        public string? FailureMessage { get; init; }

        /// <inheritdoc />
        public override string ToString() => $"IndirectProbeResponse {{ Succeeded: {Succeeded}, IntermediaryHealthScore: {IntermediaryHealthScore}, ProbeResponseTime: {ProbeResponseTime}, FailureMessage: {FailureMessage} }}";
    }
}
