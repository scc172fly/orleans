using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 分组成员设置。
    /// </summary>
    public class ClusterMembershipOptions
    {
        private TimeSpan? _minProbeTimeout;
        private TimeSpan? _maxProbeTimeout;

        /// <summary>
        /// 获取或设置表格中从一个 silo 中更新的“我还活着”的缺失数字, 从而导致警告被记录。
        /// </summary>
        /// <seealso cref="IAmAliveTablePublishTimeout"/>
        public int NumMissedTableIAmAliveLimit { get; set; } = 3;

        /// <summary>
        /// 获取或设置值,以显示是否启用了 silo 活性协议(应仅用于测试而禁用)。
        /// 如果怀疑 silo 被击落,但该财产设于 <see langword="false"/>,则不会向该系统传播或强制实施这些怀疑。
        /// 此属性仅用于测试和排除故障。
        /// 在生产过程中,应始终使生命得以存活。
        /// </summary>
        /// <value>生命默认启用。</value>
        public bool LivenessEnabled { get; set; } = true;

        /// <summary>
        /// 获取或设置生命探测器的初始超时。
        /// </summary>
        /// <remarks>
        /// 有效探矿超时和探测开始间隔期间对每次监测都独立调整 silo
        /// 依据观察到的直接-直接对等反应时间,并受 <see cref="MinProbeTimeout"/> 和
        /// <see cref="MaxProbeTimeout"/>.
        /// </remarks>
        /// <value>最初的探测超时默认为5秒。</value>
        public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 获得或设定最低有效超时值,用于活性探测。
        /// </summary>
        /// <value>半数半 <see cref="ProbeTimeout"/> 默认。</value>
        public TimeSpan MinProbeTimeout
        {
            get => _minProbeTimeout ?? TimeSpan.FromTicks(ProbeTimeout.Ticks / 2);
            set => _minProbeTimeout = value;
        }

        /// <summary>
        /// 获取或设定最大有效超时 用于活性探测器
        /// </summary>
        /// <value>默认为4倍 <see cref="ProbeTimeout"/> 。</value>
        public TimeSpan MaxProbeTimeout
        {
            get => _maxProbeTimeout ?? (ProbeTimeout.Ticks <= TimeSpan.MaxValue.Ticks / 4
                ? TimeSpan.FromTicks(ProbeTimeout.Ticks * 4)
                : TimeSpan.MaxValue);
            set => _maxProbeTimeout = value;
        }

        /// <summary>
        /// 从会籍表获取更新信息之间,可以设置或设定间隔时间。
        /// </summary>
        /// <remarks>
        /// 利用积极间隔定期更新成员资格和同行监测重新评价。
        /// </remarks>
        /// <value>成员表默认每60秒更新一次。</value>
        public TimeSpan TableRefreshTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// 获得或确定在会籍表中的表决到期时间。
        /// </summary>
        /// <value>拖欠2分钟后,票数失效。</value>
        public TimeSpan DeathVoteExpirationTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// 获取或设定在会籍表中更新此 silo心跳的时间间隔。
        /// </summary>
        /// <remarks>
        /// 这些心跳主要用于诊断目的,但也用来识别衰竭的活性
        /// 成员表格条目中的条目。此值乘以 <see cref="NumMissedTableIAmAliveLimit"/>
        /// 确定在被视作陈旧之前,从现役成员入会可持续多久而不更新心跳。
        /// </remarks>
        /// <value>默认情况下每30秒发布更新。</value>
        public TimeSpan IAmAliveTablePublishTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 获得或设定最大时间,以便在放弃前尝试加入一个组。
        /// </summary>
        /// <value>试图在默认放弃前加入5分钟。</value>
        public TimeSpan MaxJoinAttemptTime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 获得或规定没有正数 silo 几代人被划为死亡的年龄。
        /// </summary>
        /// <remarks>
        /// 使用同侪的当地世界协调时钟公布会员观点时,就确定了截断点。
        /// 现成员保留其记录状态。 设为 <see langword="null"/> ,以阻止代人退休。
        /// 观察的死亡证据保留到截止期,或者在退休时丧失工作能力的观察员的一生。
        /// 数值必须大于或等于 <see cref="TimeSpan.Zero"/>。
        /// </remarks>
        /// <value>默认一小时。</value>
        public TimeSpan? SiloGenerationRetirementAge { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// 获取或设定一个值,显示是否使用八卦在主机之间传播会员更新信息。
        /// </summary>
        /// <value>会员更新会以默认八卦方式发布。</value>
        public bool UseLivenessGossip { get; set; } = true;

        /// <summary>
        /// 获取或设置每个 silos 探测器的活性 silo 号码。
        /// </summary>
        /// <remarks>
        /// 这将决定每个主机默认情况下将监测的东道主数量 。
        /// 3等低值一般足够,如果另一个 silo 停止运行,可以迅速删除。
        /// 然而,监测费用并不昂贵,如果价值提高,在集群规模突然变化时会改善复苏。
        /// 当 silo 对另一个 silo产生怀疑时,额外的 silos 可能开始探寻该 silo ,以加快检测不起作用的 silos。
        /// </remarks>
        /// <value>每一 silo 将积极监测多达10个其他 silos 默认。</value>
        public int NumProbedSilos { get; set; } = 10;

        /// <summary>
        /// 获取或设定一个 silo 的未找到的探测请求数, 从而导致怀疑该 silo 在下方。
        /// </summary>
        /// <value>如果默认缺漏了三个探测器, A silo 将被怀疑为下方。</value>
        public int NumMissedProbesLimit { get; set; } = 3;

        /// <summary>
        /// 获取或设定宣布某些 silo 为下调(最多应为 <see cref="NumProbedSilos"/>)所需的非过期选票数。
        /// </summary>
        /// <value>以两票之二票足以 silo 默认情况下,将宣布为下方。</value>
        public int NumVotesForDeathDeclaration { get; set; } = 2;

        /// <summary>
        /// 获取或设定已死亡 silo 条目有资格被删除的留存期。
        /// 清理剂使用最新的启动、活性或可疑投票时间戳来识别过期条目。
        /// 只有在 <see cref="DefunctSiloCleanupPeriod"/> 不 <see langword="null" />时才有效。
        /// </summary>
        /// <value>缺陷 silos 违约一周后有资格被免职。</value>
        public TimeSpan DefunctSiloExpiration { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        /// 获取或设置对已死 silo 中超过 <see cref="DefunctSiloExpiration" />的条目的清理期。
        /// 当成员变动被观察到,而目前的成员快照中载有过期成员快照时,便试图清理。
        /// 或上次清理通知后这一时期已过。
        /// 将此值设为 <see langword="null"/> , 禁用过期清理 。
        /// </summary>
        /// <value>过期的基于清理默认启用。</value>
        public TimeSpan? DefunctSiloCleanupPeriod { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// 获取或设定在会籍表中保留已死亡 silo 条目的最大数量。
        /// 当超过此限制时, 第一个活动 silo中选择的 <see cref="Orleans.Runtime.SiloAddress"/>
        /// 顺序排序,不同步地删除成员表中最老的多余死亡条目。
        /// 设定此值为 <see langword="null"/> ,以保留全部已停用条目并禁用基于阈值的清理 。
        /// </summary>
        /// <value>保留最多25个已取消成员 silo 默认输入。</value>
        public int? MaxDefunctSiloEntries { get; set; } = 25;

        /// <summary>
        /// 获取或设定在会籍表中保留已死亡的 silo 条目的最短时间。
        /// 清理只清除最近启动、活性或可疑投票时间戳比这一时期长的死条目,
        /// 不论 <see cref="DefunctSiloExpiration"/> 和 <see cref="MaxDefunctSiloEntries"/>.
        /// 保留最近死条目的 silo ,其成员业务被拖延或重新审理,如加入
        /// silo 内 <see cref="MaxJoinAttemptTime"/>,观察它被宣布死亡。
        /// 这段时间应可舒适地超过 <see cref="MaxJoinAttemptTime"/> ,预期时钟在 silos之间断裂。
        /// </summary>
        /// <value>死 silo 条目默认保留至少一小时。</value>
        public TimeSpan DefunctSiloMinimumRetention { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// 如果 silo成员未更新其加入名单的心跳,则其进入名单的时间将被视为过时。
        /// </summary>
        internal TimeSpan AllowedIAmAliveMissPeriod => IAmAliveTablePublishTimeout.Multiply(NumMissedTableIAmAliveLimit);

        /// <summary>
        /// 获得等待集群成员制在关闭期间终止的所需时间。
        /// </summary>
        internal static TimeSpan ClusteringShutdownGracePeriod => TimeSpan.FromSeconds(5);

        /// <summary>
        /// 获取或规定报告当地健康退化的伐木之间的最低间隔期。
        /// </summary>
        /// <value>
        /// 当地健康每秒抽样一次,发现退化情况默认情况下每10秒钟记录一次。
        /// </value>
        public TimeSpan LocalHealthDegradationMonitoringPeriod { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 获取或设定一个数值,表明是否根据当前当地健康状况恶化情况延长有效探测超时。
        /// </summary>
        public bool ExtendProbeTimeoutDuringDegradation { get; set; } = true;

        /// <summary>
        /// 获取或设定一个值,以显示是否启用验证 silos 间接,通过其他途径间接、 silos.
        /// </summary>
        public bool EnableIndirectProbes { get; set; } = true;

        /// <summary>
        /// 获取或设定一个值,表明在评价 silo 是否考虑连接级信息活动。
        /// </summary>
        /// <remarks>
        /// 启用时, 如果与 silo 的主动连接在监测窗口中最近收到消息
        /// <see cref="MaxProbeTimeout"/> x <see cref="NumMissedProbesLimit"/>,投票怀疑 silo 将予压制
        /// 原因是连接活动证明 silo 还活着,这有助于防止虚假死亡申报
        /// 当探测由于诸如GC暂停或线性池饱和等本地问题而失败时。
        /// </remarks>
        /// <value>连接实时检查默认启用。</value>
        public bool EnableConnectionLivenessCheck { get; set; } = true;

        /// <summary>
        /// 获取或设定一个数值,表明是否允许驱逐 silos 他们逗留在火狱里, <c>Joining</c> 或 <c>Created</c> 状态超过 <see cref="MaxJoinAttemptTime"/>.
        /// </summary>
        public bool EvictWhenMaxJoinAttemptTimeExceeded { get; set; } = true;
    }
}
