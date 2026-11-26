using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Core.Messaging;
using Orleans.Statistics;

namespace Orleans.Runtime
{
    /// <summary>
    /// silo当前运行时统计的快照
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class SiloRuntimeStatistics
    {
        /// <summary>
        /// silo中启动的总数。
        /// </summary>
        [Id(0)]
        public int ActivationCount { get; }

        /// <summary>
        /// 最近使用的 silo 中激活的数量。
        /// </summary>
        [Id(1)]
        public int RecentlyUsedActivationCount { get; }

        /// <summary>
        /// CPU的利用率
        /// </summary>
        [Id(2), Obsolete($"The will be removed, use {nameof(EnvironmentStatistics)}.{nameof(EnvironmentStatistics.FilteredCpuUsagePercentage)} instead.", error: false)]
        public float? CpuUsage { get; }

        /// <summary>
        /// silo [字节]中可用内存量。
        /// </summary>
        [Id(3), Obsolete($"The will be removed, use {nameof(EnvironmentStatistics)}.{nameof(EnvironmentStatistics.FilteredAvailableMemoryBytes)} instead.", error: false)]
        public float? AvailableMemory { get; }

        /// <summary>
        /// 使用的内存大小 。
        /// </summary>
        [Id(4), Obsolete($"The will be removed, use {nameof(EnvironmentStatistics)}.{nameof(EnvironmentStatistics.FilteredMemoryUsageBytes)} instead.", error: false)]
        public long? MemoryUsage { get; }

        /// <summary>
        /// 可用物理内存总数 [字节] 。
        /// </summary>
        [Id(5), Obsolete($"The will be removed, use {nameof(EnvironmentStatistics)}.{nameof(EnvironmentStatistics.MaximumAvailableMemoryBytes)} instead.", error: false)]
        public long? TotalPhysicalMemory { get; }

        /// <summary>
        /// 这是这个 silo 超载
        /// </summary>
        [Id(6)]
        public bool IsOverloaded { get; }

        /// <summary>
        /// 目前与该 silo有关的客户数目。
        /// </summary>
        [Id(7)]
        public long ClientCount { get; }

        /// <summary>
        /// silo收到的信息的累积大小,以字节计。
        /// </summary>
        /// <remarks>
        /// 使用 <c>orleans-messaging-received-messages-size</c> 直方图通过 OpenTelemetry
        /// 获取收藏开始以来收到的字节。
        /// </remarks>
        [Id(8), Obsolete("Use the orleans-messaging-received-messages-size histogram through OpenTelemetry instead.")]
        public long ReceivedMessages { get; }

        /// <summary>
        /// silo发送的信件的累积大小,以字节计。
        /// </summary>
        /// <remarks>
        /// 使用 <c>orleans-messaging-sent-messages-size</c> 直方图通过 OpenTelemetry
        /// 获取收藏开始后发送的字节。
        /// </remarks>
        [Id(9), Obsolete("Use the orleans-messaging-sent-messages-size histogram through OpenTelemetry instead.")]
        public long SentMessages { get; }

        /// <summary>
        /// 创建此统计时的 DateTime 。
        /// </summary>
        [Id(10)]
        public DateTime DateTime { get; }

        /// <summary>
        /// 获取 silo的环境资源利用统计数据。
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
#pragma warning disable CS0618 // Preserve the legacy values in the serialized statistics contract.
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
#pragma warning disable CS0618 // Preserve the legacy fields in the diagnostic representation.
        public override string ToString() => @$"SiloRuntimeStatistics: ActivationCount={ActivationCount} RecentlyUsedActivationCount={RecentlyUsedActivationCount} CpuUsagePercentage={EnvironmentStatistics.FilteredCpuUsagePercentage} MemoryUsageBytes={EnvironmentStatistics.FilteredMemoryUsageBytes} AvailableMemory={EnvironmentStatistics.FilteredAvailableMemoryBytes} MaximumAvailableMemoryBytes={EnvironmentStatistics.MaximumAvailableMemoryBytes} IsOverloaded={IsOverloaded} ClientCount={ClientCount} ReceivedMessages={ReceivedMessages} SentMessages={SentMessages} DateTime={DateTime}";
#pragma warning restore CS0618
    }

    /// <summary>
    /// 特定对象当前统计数据简简简简简 grain 指定类型上的 silo.
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class SimpleGrainStatistic
    {
        /// <summary>
        /// 类型 grain 为此 SimpleGrainStatistic.
        /// </summary>
        [Id(0)]
        public string GrainType { get; init; } = default!;

        /// <summary>
        /// 下 silo 用于此地址的地址 SimpleGrainStatistic.
        /// </summary>
        [Id(1)]
        public SiloAddress SiloAddress { get; init; } = default!;

        /// <summary>
        /// 此启动次数 grain 给定的此类型 silo.
        /// </summary>
        [Id(2)]
        public int ActivationCount { get; init; }

        /// <summary>
        /// 返回此 SimpleGrainStatistic的字符串表示。
        /// </summary>
        public override string ToString() => $"SimpleGrainStatistic: GrainType={GrainType} Silo={SiloAddress} NumActivations={ActivationCount} ";
    }

    /// <summary>
    /// 代表活跃的 grain 及其在组内的位置。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class DetailedGrainStatistic
    {
        /// <summary>
        /// 类型 grain 为此 DetailedGrainStatistic.
        /// </summary>
        [Id(0)]
        public string GrainType { get; init; } = default!;

        /// <summary>
        /// 下 silo 用于此地址的地址 DetailedGrainStatistic.
        /// </summary>
        [Id(1)]
        public SiloAddress SiloAddress { get; init; } = default!;

        /// <summary>
        /// grain独有的 Id 。
        /// </summary>
        [Id(2)]
        public GrainId GrainId { get; init; }
    }


    [Serializable, GenerateSerializer, Immutable]
    internal sealed class DetailedGrainReport
    {
        [Id(0)]
        public GrainId Grain { get; init; }

        /// <summary>silo 这些统计数字来自</summary>
        [Id(1)]
        public SiloAddress SiloAddress { get; init; } = default!;

        /// <summary>silo 这些统计数字来自</summary>
        [Id(2)]
        public string SiloName { get; init; } = default!;

        /// <summary>本地目录缓存中激活地址</summary>
        [Id(3)]
        public GrainAddress? LocalCacheActivationAddress { get; init; }

        /// <summary>本地目录中的激活地址。</summary>
        [Id(4)]
        public GrainAddress? LocalDirectoryActivationAddress { get; init; }

        /// <summary>小学 silo 为此 grain</summary>
        [Id(5)]
        public SiloAddress? PrimaryForGrain { get; init; }

        /// <summary>执行此 grain的类别的名称。</summary>
        [Id(6)]
        public string? GrainClassTypeName { get; init; }

        /// <summary>激活此 silo</summary>
        [Id(7)]
        public string? LocalActivation { get; init; }

        public override string ToString() => @$"{Environment.NewLine}**DetailedGrainReport for grain {Grain} from silo {SiloName} SiloAddress={SiloAddress}{Environment.NewLine}   LocalCacheActivationAddresses={LocalCacheActivationAddress}{Environment.NewLine}   LocalDirectoryActivationAddresses={LocalDirectoryActivationAddress}{Environment.NewLine}   PrimaryForGrain={PrimaryForGrain}{Environment.NewLine}   GrainClassTypeName={GrainClassTypeName}{Environment.NewLine}   LocalActivation:{Environment.NewLine}{LocalActivation}.{Environment.NewLine}";
    }
}
