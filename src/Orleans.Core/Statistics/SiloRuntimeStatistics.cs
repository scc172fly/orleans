using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Core.Messaging;
using Orleans.Statistics;

namespace Orleans.Runtime
{
    /// <summary>
    /// Silo 当前运行时统计信息的快照。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class SiloRuntimeStatistics
    {
        /// <summary>
        /// Silo 中激活实例的总数。
        /// </summary>
        [Id(0)]
        public int ActivationCount { get; }

        /// <summary>
        /// Silo 中最近使用过的激活实例数量。
        /// </summary>
        [Id(1)]
        public int RecentlyUsedActivationCount { get; }

        /// <summary>
        /// CPU 利用率。
        /// </summary>
        [Id(2), Obsolete($"The will be removed, use {nameof(EnvironmentStatistics)}.{nameof(EnvironmentStatistics.FilteredCpuUsagePercentage)} instead.", error: false)]
        public float? CpuUsage { get; }

        /// <summary>
        /// Silo 中可用的内存量（以字节为单位）。
        /// </summary>
        [Id(3), Obsolete($"The will be removed, use {nameof(EnvironmentStatistics)}.{nameof(EnvironmentStatistics.FilteredAvailableMemoryBytes)} instead.", error: false)]
        public float? AvailableMemory { get; }

        /// <summary>
        /// 已使用的内存大小。
        /// </summary>
        [Id(4), Obsolete($"The will be removed, use {nameof(EnvironmentStatistics)}.{nameof(EnvironmentStatistics.FilteredMemoryUsageBytes)} instead.", error: false)]
        public long? MemoryUsage { get; }

        /// <summary>
        /// 可用物理内存总量（以字节为单位）。
        /// </summary>
        [Id(5), Obsolete($"The will be removed, use {nameof(EnvironmentStatistics)}.{nameof(EnvironmentStatistics.MaximumAvailableMemoryBytes)} instead.", error: false)]
        public long? TotalPhysicalMemory { get; }

        /// <summary>
        /// 此 Silo 是否过载。
        /// </summary>
        [Id(6)]
        public bool IsOverloaded { get; }

        /// <summary>
        /// 当前连接到该 Silo 的客户端数量。
        /// </summary>
        [Id(7)]
        public long ClientCount { get; }

        /// <summary>
        /// Silo 已接收消息的累计大小（以字节为单位）。
        /// </summary>
        /// <remarks>
        /// 通过 OpenTelemetry 使用 <c>orleans-messaging-received-messages-size</c> 直方图的总和，
        /// 获取自开始收集以来已接收的字节数。
        /// </remarks>
        [Id(8), Obsolete("Use the orleans-messaging-received-messages-size histogram through OpenTelemetry instead.")]
        public long ReceivedMessages { get; }

        /// <summary>
        /// Silo 已发送消息的累计大小（以字节为单位）。
        /// </summary>
        /// <remarks>
        /// 通过 OpenTelemetry 使用 <c>orleans-messaging-sent-messages-size</c> 直方图的总和，
        /// 获取自开始收集以来已发送的字节数。
        /// </remarks>
        [Id(9), Obsolete("Use the orleans-messaging-sent-messages-size histogram through OpenTelemetry instead.")]
        public long SentMessages { get; }

        /// <summary>
        /// 创建此统计信息时的 DateTime。
        /// </summary>
        [Id(10)]
        public DateTime DateTime { get; }

        /// <summary>
        /// 获取 Silo 的环境资源利用率统计信息。
        /// </summary>
        [Id(11)]
        public EnvironmentStatistics EnvironmentStatistics { get; }

        internal SiloRuntimeStatistics(
            int activationCount,
            int recentlyUsedActivationCount,
            IEnvironmentStatisticsProvider environmentStatisticsProvider,
            IOptions<LoadSheddingOptions> loadSheddingOptions,
            long clientCount,
            DateTime dateTime)
        {
            ActivationCount = activationCount;
            RecentlyUsedActivationCount = recentlyUsedActivationCount;
            ClientCount = clientCount;
#pragma warning disable CS0618 // 在序列化统计信息契约中保留旧版值。
            ReceivedMessages = SiloRuntimeMetricsListener.MessageReceivedTotal;
            SentMessages = SiloRuntimeMetricsListener.MessageSentTotal;
#pragma warning restore CS0618
            DateTime = dateTime;

            var statistics = environmentStatisticsProvider.GetEnvironmentStatistics();

            EnvironmentStatistics = statistics;
            IsOverloaded = loadSheddingOptions.Value.LoadSheddingEnabled && OverloadDetectionLogic.IsOverloaded(ref statistics, loadSheddingOptions.Value);

#pragma warning disable 618
            CpuUsage = statistics.FilteredCpuUsagePercentage;
            MemoryUsage = statistics.FilteredMemoryUsageBytes;
            AvailableMemory = statistics.FilteredAvailableMemoryBytes;
            TotalPhysicalMemory = statistics.MaximumAvailableMemoryBytes;
#pragma warning restore 618
        }

        /// <inheritdoc/>
#pragma warning disable CS0618 // 在诊断表示形式中保留旧版字段。
        public override string ToString() => @$"SiloRuntimeStatistics: ActivationCount={ActivationCount} RecentlyUsedActivationCount={RecentlyUsedActivationCount} CpuUsagePercentage={EnvironmentStatistics.FilteredCpuUsagePercentage} MemoryUsageBytes={EnvironmentStatistics.FilteredMemoryUsageBytes} AvailableMemory={EnvironmentStatistics.FilteredAvailableMemoryBytes} MaximumAvailableMemoryBytes={EnvironmentStatistics.MaximumAvailableMemoryBytes} IsOverloaded={IsOverloaded} ClientCount={ClientCount} ReceivedMessages={ReceivedMessages} SentMessages={SentMessages} DateTime={DateTime}";
#pragma warning restore CS0618
    }

    /// <summary>
    /// 指定 Silo 上指定 Grain 类型当前统计信息的简单快照。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class SimpleGrainStatistic
    {
        /// <summary>
        /// 此 SimpleGrainStatistic 对应的 Grain 类型。
        /// </summary>
        [Id(0)]
        public string GrainType { get; init; } = default!;

        /// <summary>
        /// 此 SimpleGrainStatistic 对应的 Silo 地址。
        /// </summary>
        [Id(1)]
        public SiloAddress SiloAddress { get; init; } = default!;

        /// <summary>
        /// 此 Grain 类型在指定 Silo 上的激活实例数量。
        /// </summary>
        [Id(2)]
        public int ActivationCount { get; init; }

        /// <summary>
        /// 返回此 SimpleGrainStatistic 的字符串表示形式。
        /// </summary>
        public override string ToString() => $"SimpleGrainStatistic: GrainType={GrainType} Silo={SiloAddress} NumActivations={ActivationCount} ";
    }

    /// <summary>
    /// 表示处于活动状态的 Grain 及其在集群中的位置。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class DetailedGrainStatistic
    {
        /// <summary>
        /// 此 DetailedGrainStatistic 对应的 Grain 类型。
        /// </summary>
        [Id(0)]
        public string GrainType { get; init; } = default!;

        /// <summary>
        /// 此 DetailedGrainStatistic 对应的 Silo 地址。
        /// </summary>
        [Id(1)]
        public SiloAddress SiloAddress { get; init; } = default!;

        /// <summary>
        /// Grain 的唯一 ID。
        /// </summary>
        [Id(2)]
        public GrainId GrainId { get; init; }
    }


    [Serializable, GenerateSerializer, Immutable]
    internal sealed class DetailedGrainReport
    {
        [Id(0)]
        public GrainId Grain { get; init; }

        /// <summary>这些统计信息来源的 Silo。</summary>
        [Id(1)]
        public SiloAddress SiloAddress { get; init; } = default!;

        /// <summary>这些统计信息来源的 Silo。</summary>
        [Id(2)]
        public string SiloName { get; init; } = default!;

        /// <summary>本地目录缓存中的激活实例地址。</summary>
        [Id(3)]
        public GrainAddress? LocalCacheActivationAddress { get; init; }

        /// <summary>本地目录中的激活实例地址。</summary>
        [Id(4)]
        public GrainAddress? LocalDirectoryActivationAddress { get; init; }

        /// <summary>此 Grain 对应的主 Silo。</summary>
        [Id(5)]
        public SiloAddress? PrimaryForGrain { get; init; }

        /// <summary>实现此 Grain 的类的名称。</summary>
        [Id(6)]
        public string? GrainClassTypeName { get; init; }

        /// <summary>此 Silo 上的激活实例。</summary>
        [Id(7)]
        public string? LocalActivation { get; init; }

        public override string ToString() => @$"{Environment.NewLine}**DetailedGrainReport for grain {Grain} from silo {SiloName} SiloAddress={SiloAddress}{Environment.NewLine}   LocalCacheActivationAddresses={LocalCacheActivationAddress}{Environment.NewLine}   LocalDirectoryActivationAddresses={LocalDirectoryActivationAddress}{Environment.NewLine}   PrimaryForGrain={PrimaryForGrain}{Environment.NewLine}   GrainClassTypeName={GrainClassTypeName}{Environment.NewLine}   LocalActivation:{Environment.NewLine}{LocalActivation}.{Environment.NewLine}";
    }
}
