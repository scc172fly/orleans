using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 集群成员资格相关设置。
    /// </summary>
    public class ClusterMembershipOptions
    {
        private TimeSpan? _minProbeTimeout;
        private TimeSpan? _maxProbeTimeout;

        /// <summary>
        /// 获取或设置 Silo 在成员表中连续缺少多少次“我仍存活”更新后记录警告。
        /// </summary>
        /// <seealso cref="IAmAliveTablePublishTimeout"/>
        public int NumMissedTableIAmAliveLimit { get; set; } = 3;

        /// <summary>
        /// 获取或设置是否启用 Silo 存活检测协议（仅应在测试时禁用）。
        /// 如果怀疑某个 Silo 已下线，但此属性设为 <see langword="false"/>，则不会向系统传播或执行该怀疑结果。
        /// 此属性仅用于测试和故障排查。
        /// 在生产环境中应始终启用存活检测。
        /// </summary>
        /// <value>默认启用存活检测。</value>
        public bool LivenessEnabled { get; set; } = true;

        /// <summary>
        /// 获取或设置存活探测的初始超时时间。
        /// </summary>
        /// <remarks>
        /// 每个被监控 Silo 的实际探测超时时间及两次探测启动之间的间隔独立调整，
        /// 根据观测到的直接探测响应时间确定，且受 <see cref="MinProbeTimeout"/> 和
        /// <see cref="MaxProbeTimeout"/>.
        /// </remarks>
        /// <value>初始探测超时默认为 5 秒。</value>
        public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 获取或设置存活探测的最小实际超时时间。
        /// </summary>
        /// <value>默认为 <see cref="ProbeTimeout"/> 的一半。</value>
        public TimeSpan MinProbeTimeout
        {
            get => _minProbeTimeout ?? TimeSpan.FromTicks(ProbeTimeout.Ticks / 2);
            set => _minProbeTimeout = value;
        }

        /// <summary>
        /// 获取或设置存活探测的最大实际超时时间。
        /// </summary>
        /// <value>默认为 <see cref="ProbeTimeout"/> 的四倍。</value>
        public TimeSpan MaxProbeTimeout
        {
            get => _maxProbeTimeout ?? (ProbeTimeout.Ticks <= TimeSpan.MaxValue.Ticks / 4
                ? TimeSpan.FromTicks(ProbeTimeout.Ticks * 4)
                : TimeSpan.MaxValue);
            set => _maxProbeTimeout = value;
        }

        /// <summary>
        /// 获取或设置从成员表获取更新的时间间隔。
        /// </summary>
        /// <remarks>
        /// 对于周期性成员表刷新及对等节点监控的重新评估，请使用正数时间间隔。
        /// </remarks>
        /// <value>默认每 60 秒刷新一次成员表。</value>
        public TimeSpan TableRefreshTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// 获取或设置成员表中投票的过期时间。
        /// </summary>
        /// <value>投票默认在 2 分钟后过期。</value>
        public TimeSpan DeathVoteExpirationTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// 获取或设置此 Silo 在成员表中更新心跳的间隔。
        /// </summary>
        /// <remarks>
        /// 这些心跳主要用于诊断，也用于识别成员表中已陈旧的活跃
        /// 条目。此值乘以 <see cref="NumMissedTableIAmAliveLimit"/>
        /// 即为活跃成员条目在被视为陈旧之前允许不更新心跳的时间。
        /// </remarks>
        /// <value>默认每 30 秒发布一次更新。</value>
        public TimeSpan IAmAliveTablePublishTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 获取或设置尝试加入集群的最长时间，超过该时间后放弃。
        /// </summary>
        /// <value>默认尝试加入 5 分钟后放弃。</value>
        public TimeSpan MaxJoinAttemptTime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 获取或设置缺失且世代编号为正的 Silo 被判定为已死亡时的年龄阈值。
        /// </summary>
        /// <remarks>
        /// 发布成员资格视图时，以对等节点的本地 UTC 时钟确定截止值。
        /// 仍存在的成员保留其记录中的状态。设为 <see langword="null"/> 可禁用世代淘汰。
        /// 已观测到的死亡证据保留到被截止值覆盖为止；禁用淘汰时，则保留至观察者生命周期结束。
        /// 此值必须大于或等于 <see cref="TimeSpan.Zero"/>。
        /// </remarks>
        /// <value>默认为一小时。</value>
        public TimeSpan? SiloGenerationRetirementAge { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// 获取或设置是否通过 Gossip 协议在宿主之间传播成员资格更新。
        /// </summary>
        /// <value>默认使用 Gossip 协议传播成员资格更新。</value>
        public bool UseLivenessGossip { get; set; } = true;

        /// <summary>
        /// 获取或设置每个 Silo 进行存活探测的 Silo 数量。
        /// </summary>
        /// <remarks>
        /// 这决定了每个宿主默认监控的宿主数量。
        /// 较小的值（如 3）通常已足够，并能在其他 Silo 停止工作时及时将其移除。
        /// 但监控开销并不大，较大的值可在集群规模突然变化时改善恢复能力。
        /// 当某个 Silo 怀疑另一个 Silo 时，更多 Silo 可能开始探测该 Silo，以加快检测失效 Silo 的速度。
        /// </remarks>
        /// <value>默认每个 Silo 主动监控最多 10 个其他 Silo。</value>
        public int NumProbedSilos { get; set; } = 10;

        /// <summary>
        /// 获取或设置未收到某个 Silo 探测响应的次数阈值，达到该阈值后怀疑该 Silo 已下线。
        /// </summary>
        /// <value>默认连续缺少三次探测响应后，怀疑 Silo 已下线。</value>
        public int NumMissedProbesLimit { get; set; } = 3;

        /// <summary>
        /// 获取或设置宣告某个 Silo 已下线所需的未过期投票数（应不超过 <see cref="NumProbedSilos"/>）。
        /// </summary>
        /// <value>默认两票即可宣告一个 Silo 已下线。</value>
        public int NumVotesForDeathDeclaration { get; set; } = 2;

        /// <summary>
        /// 获取或设置已死亡 Silo 条目可被移除前的保留期。
        /// 清理代理根据最近的启动、存活或怀疑投票时间戳识别过期条目。
        /// 仅在 <see cref="DefunctSiloCleanupPeriod"/> 不为 <see langword="null" /> 时有效。
        /// </summary>
        /// <value>默认已失效的 Silo 在一周后符合从成员表移除的条件。</value>
        public TimeSpan DefunctSiloExpiration { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        /// 获取或设置清理年龄超过 <see cref="DefunctSiloExpiration" /> 的已死亡 Silo 条目的周期。
        /// 当观察到成员资格变更且当前成员资格快照包含过期的
        /// 已死亡条目时，或距上次清理调用已经过此周期时，尝试清理。
        /// 将此值设为 <see langword="null"/> 可禁用基于过期时间的清理。
        /// </summary>
        /// <value>默认启用基于过期时间的清理。</value>
        public TimeSpan? DefunctSiloCleanupPeriod { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// 获取或设置成员表中最多保留的已死亡 Silo 条目数。
        /// 超出此上限时，按 <see cref="Orleans.Runtime.SiloAddress"/> 的自然
        /// 排序选出的第一个活跃 Silo 会异步从成员表中移除最早的超额死亡条目。
        /// 将此值设为 <see langword="null"/> 可保留所有失效条目并禁用基于数量阈值的清理。
        /// </summary>
        /// <value>成员表默认最多保留 25 个已失效的 Silo 条目。</value>
        public int? MaxDefunctSiloEntries { get; set; } = 25;

        /// <summary>
        /// 获取或设置已死亡 Silo 条目在成员表中的最短保留时间。
        /// 清理仅移除最近的启动、存活或怀疑投票时间戳早于该时限的已死亡条目，
        /// 不受 <see cref="DefunctSiloExpiration"/> 和 <see cref="MaxDefunctSiloEntries"/> 的影响。
        /// 保留近期死亡条目可使成员资格操作延迟或重试的 Silo（例如正在加入且
        /// 处于 <see cref="MaxJoinAttemptTime"/> 时限内的 Silo）观察到自己已被宣告死亡。
        /// 此时限应充分超过 <see cref="MaxJoinAttemptTime"/> 及 Silo 之间预期的时钟偏差。
        /// </summary>
        /// <value>默认至少保留已死亡 Silo 条目一小时。</value>
        public TimeSpan DefunctSiloMinimumRetention { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// 获取 Silo 未更新成员表中的心跳时，其成员条目被视为陈旧所需经过的时间。
        /// </summary>
        internal TimeSpan AllowedIAmAliveMissPeriod => IAmAliveTablePublishTimeout.Multiply(NumMissedTableIAmAliveLimit);

        /// <summary>
        /// 获取关闭期间等待集群成员资格系统终止的时间。
        /// </summary>
        internal static TimeSpan ClusteringShutdownGracePeriod => TimeSpan.FromSeconds(5);

        /// <summary>
        /// 获取或设置记录本地健康状况恶化日志的最短间隔。
        /// </summary>
        /// <value>
        /// 默认每秒采样一次本地健康状况，检测到恶化时最多每十秒记录一次日志。
        /// </value>
        public TimeSpan LocalHealthDegradationMonitoringPeriod { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 获取或设置是否根据当前本地健康状况的恶化程度延长实际探测超时时间。
        /// </summary>
        public bool ExtendProbeTimeoutDuringDegradation { get; set; } = true;

        /// <summary>
        /// 获取或设置是否启用通过其他 Silo 间接探测 Silo 的机制。
        /// </summary>
        public bool EnableIndirectProbes { get; set; } = true;

        /// <summary>
        /// 获取或设置评估 Silo 存活性时是否考虑连接层面的消息活动。
        /// </summary>
        /// <remarks>
        /// 启用后，如果到某个 Silo 的活跃连接近期在监控窗口内接收过消息
        /// （<see cref="MaxProbeTimeout"/> × <see cref="NumMissedProbesLimit"/>），则会抑制对该 Silo 的怀疑投票，
        /// 因为连接活动表明该 Silo 仍然存活。这样可在
        /// 因 GC 暂停或线程池饱和等本地问题导致探测失败时避免误判死亡。
        /// </remarks>
        /// <value>默认启用连接存活检查。</value>
        public bool EnableConnectionLivenessCheck { get; set; } = true;

        /// <summary>
        /// 获取或设置是否在 Silo 处于 <c>Joining</c> 或 <c>Created</c> 状态超过 <see cref="MaxJoinAttemptTime"/> 时将其逐出成员表。
        /// </summary>
        public bool EvictWhenMaxJoinAttemptTimeExceeded { get; set; } = true;
    }
}
