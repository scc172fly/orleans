using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Providers;

namespace Orleans.Runtime
{
    /// <summary>
    /// Silo 系统管理功能的接口，
    /// 以 Grain 形式公开，用于接收远程请求或命令。
    /// </summary>
    public interface IManagementGrain : IGrainWithIntegerKey, IVersionManager
    {
        /// <summary>
        /// 停用当前所有活动 Silo 上指定 Grain 的现有激活实例。
        /// </summary>
        /// <remarks>
        /// 此操作绝不会激活 Grain 或读取其持久化状态。它包括无状态工作者；
        /// 如果不存在激活实例，则不执行任何操作。它不会阻止后续请求激活 Grain。
        /// </remarks>
        /// <param name="grainId">要停用的 Grain 的确切标识。</param>
        /// <param name="cancellationToken">取消对操作完成的等待。已请求的停用操作会继续执行。</param>
        /// <returns>在匹配的激活实例完成停用后完成的任务。</returns>
        [Alias("DeactivateGrain")]
        Task DeactivateGrain(GrainId grainId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取此集群目前已知的 Silo 主机及其状态的列表。
        /// </summary>
        /// <param name="onlyActive">是否仅返回当前活动 Silo 的数据，
        /// 或者按默认行为返回所有当前及过去的 Silo 实例的数据（包括处于 Joining 或 Dead 状态的实例）。</param>
        /// <returns>主机及其对应状态。</returns>
        [Alias("GetHosts")]
        Task<Dictionary<SiloAddress, SiloStatus>> GetHosts(bool onlyActive = false);

        [Alias("4C0864C2")]
        Task<Dictionary<SiloAddress, SiloStatus>> GetHosts(bool onlyActive, CancellationToken cancellationToken)
            => GetHosts(onlyActive);

        /// <summary>
        /// 获取此集群目前已知的 Silo 主机及成员资格信息的列表。
        /// </summary>
        /// <param name="onlyActive">是否仅返回当前活动 Silo 的数据，
        /// 或者按默认行为返回所有当前及过去的 Silo 实例的数据（包括处于 Joining 或 Dead 状态的实例）。</param>
        /// <returns>主机条目。</returns>
        [Alias("GetDetailedHosts")]
        Task<MembershipEntry[]> GetDetailedHosts(bool onlyActive = false);

        [Alias("CC6CCBC3")]
        Task<MembershipEntry[]> GetDetailedHosts(bool onlyActive, CancellationToken cancellationToken)
            => GetDetailedHosts(onlyActive);

        /// <summary>
        /// 在指定 Silo 中执行一次 .NET 垃圾回收。
        /// </summary>
        /// <param name="hostsIds">此命令将发送到的 Silo 列表。</param>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        [Alias("ForceGarbageCollection")]
        Task ForceGarbageCollection(SiloAddress[] hostsIds);

        [Alias("5922EB76")]
        Task ForceGarbageCollection(SiloAddress[] hostsIds, CancellationToken cancellationToken)
            => ForceGarbageCollection(hostsIds);

        /// <summary>在指定 Silo 中执行一次 Orleans 激活实例回收。</summary>
        /// <param name="hostsIds">此命令将发送到的 Silo 列表。</param>
        /// <param name="ageLimit">待回收激活实例的最大空闲时间。</param>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        [Alias("ForceActivationCollectionOnSilos")]
        Task ForceActivationCollection(SiloAddress[] hostsIds, TimeSpan ageLimit);

        [Alias("329F9A1B")]
        Task ForceActivationCollection(SiloAddress[] hostsIds, TimeSpan ageLimit, CancellationToken cancellationToken)
            => ForceActivationCollection(hostsIds, ageLimit);

        /// <summary>
        /// 强制执行激活实例回收。
        /// </summary>
        /// <param name="ageLimit">空闲时限。空闲时间超过此期限的 Grain 激活实例将符合回收条件。</param>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        [Alias("ForceActivationCollection")]
        Task ForceActivationCollection(TimeSpan ageLimit);

        [Alias("54E6D1D1")]
        Task ForceActivationCollection(TimeSpan ageLimit, CancellationToken cancellationToken)
            => ForceActivationCollection(ageLimit);

        /// <summary>在指定 Silo 中执行一次 Silo 统计信息收集。</summary>
        /// <param name="siloAddresses">此命令将发送到的 Silo 列表。</param>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        [Alias("ForceRuntimeStatisticsCollection")]
        Task ForceRuntimeStatisticsCollection(SiloAddress[] siloAddresses);

        [Alias("B761B345")]
        Task ForceRuntimeStatisticsCollection(SiloAddress[] siloAddresses, CancellationToken cancellationToken)
            => ForceRuntimeStatisticsCollection(siloAddresses);

        /// <summary>
        /// 返回指定 Silo 最新的运行时统计信息。
        /// </summary>
        /// <param name="hostsIds">此命令将发送到的 Silo 列表。</param>
        /// <returns>指定主机的运行时统计信息。</returns>
        [Alias("GetRuntimeStatistics")]
        Task<SiloRuntimeStatistics[]> GetRuntimeStatistics(SiloAddress[] hostsIds);

        [Alias("2D761B36")]
        Task<SiloRuntimeStatistics[]> GetRuntimeStatistics(SiloAddress[] hostsIds, CancellationToken cancellationToken)
            => GetRuntimeStatistics(hostsIds);

        /// <summary>
        /// 返回跨 Silo 汇总的最新 Grain 统计信息。
        /// </summary>
        /// <param name="hostsIds">此命令将发送到的 Silo 列表。</param>
        /// <returns>指定主机的简单 Grain 统计信息。</returns>
        [Alias("GetSimpleGrainStatisticsOnSilos")]
        Task<SimpleGrainStatistic[]> GetSimpleGrainStatistics(SiloAddress[] hostsIds);

        [Alias("3CFF788C")]
        Task<SimpleGrainStatistic[]> GetSimpleGrainStatistics(SiloAddress[] hostsIds, CancellationToken cancellationToken)
            => GetSimpleGrainStatistics(hostsIds);

        /// <summary>
        /// 返回跨所有 Silo 汇总的最新 Grain 统计信息。
        /// </summary>
        /// <returns>简单 Grain 统计信息。</returns>
        [Alias("GetSimpleGrainStatistics")]
        Task<SimpleGrainStatistic[]> GetSimpleGrainStatistics();

        [Alias("ACCE9D6A")]
        Task<SimpleGrainStatistic[]> GetSimpleGrainStatistics(CancellationToken cancellationToken)
            => GetSimpleGrainStatistics();

        /// <summary>
        /// 返回跨 Silo 汇总的指定类型最新详细 Grain 统计信息。
        /// </summary>
        /// <param name="hostsIds">此命令将发送到的 Silo 列表。</param>
        /// <param name="types">用于筛选结果的 Grain 类型数组。</param>
        /// <returns>详细 Grain 统计信息。</returns>
        [Alias("GetDetailedGrainStatistics")]
        Task<DetailedGrainStatistic[]> GetDetailedGrainStatistics(string[]? types = null, SiloAddress[]? hostsIds = null);

        [Alias("0A1C0D82")]
        Task<DetailedGrainStatistic[]> GetDetailedGrainStatistics(
            string[]? types,
            SiloAddress[]? hostsIds,
            CancellationToken cancellationToken)
            => GetDetailedGrainStatistics(types, hostsIds);
        /// <summary>
        /// 获取特定 Grain 类型的激活实例数量。
        /// </summary>
        /// <param name="grainReference">Grain 引用。</param>
        /// <returns>获取与所提供 Grain 引用具有相同类型的 Grain 激活实例数量。</returns>
        [Alias("GetGrainActivationCount")]
        Task<int> GetGrainActivationCount(GrainReference grainReference);

        [Alias("AEDE93F6")]
        Task<int> GetGrainActivationCount(GrainReference grainReference, CancellationToken cancellationToken)
            => GetGrainActivationCount(grainReference);
        /// <summary>
        /// 返回所有 Silo 中当前所有 Grain 激活实例的总数。
        /// </summary>
        /// <returns>所有 Silo 中 Grain 激活实例的总数。</returns>
        [Alias("GetTotalActivationCount")]
        Task<int> GetTotalActivationCount();

        [Alias("D7365B43")]
        Task<int> GetTotalActivationCount(CancellationToken cancellationToken)
            => GetTotalActivationCount();

        /// <summary>
        /// 在集群所有 Silo 上的指定提供程序中执行控制命令。
        /// 将命令发送到各 Silo 上同时匹配 <c>providerTypeFullName</c> 和 <c>providerName</c> 两个参数的所有已知提供程序。
        /// </summary>
        /// <remarks>
        /// 提供程序必须实现 <c>Orleans.Providers.IControllable</c>
        /// 接口，才能接收这些控制通道命令。
        /// </remarks>
        /// <param name="providerName">要接收此命令的提供程序名称。</param>
        /// <param name="command">此命令的 ID 或序号。
        /// 对 Orleans 运行时而言，这是一个不透明的值；控制协议的语义由发送方和提供程序约定。</param>
        /// <param name="arg">不透明的命令参数。
        /// 对 Orleans 运行时而言，这是一个不透明的值；控制协议的语义由发送方和提供程序约定。</param>
        /// <returns>表示此操作完成情况的异步任务。</returns>
        [Alias("SendControlCommandToProvider")]
        public Task<object?[]> SendControlCommandToProvider<T>(string providerName, int command, object? arg = null) where T : IControllable;

        [Alias("F67965CC")]
        public Task<object?[]> SendControlCommandToProvider<T>(
            string providerName,
            int command,
            object? arg,
            CancellationToken cancellationToken) where T : IControllable
            => SendControlCommandToProvider<T>(providerName, command, arg);

        /// <summary>
        /// 返回指定 Grain 已激活的 <see cref="Orleans.Runtime.SiloAddress"/>（如果存在）。
        /// </summary>
        /// <remarks>
        /// 请注意，此方法并不提供 Grain 激活实例目录（Catalog）的强一致性视图。
        /// 此方法的返回值基于 Grain 最后已知的状态；调用方收到请求结果时，该状态可能已不是最新状态。
        /// </remarks>
        /// <param name="reference">要查找的 <see cref="Orleans.Runtime.IAddressable"/>。</param>
        /// <returns>根据 Grain 激活实例目录最后已知状态的快照返回 Grain 已激活的 <see cref="Orleans.Runtime.SiloAddress"/>；如果未激活，则返回 null。</returns>
        [Alias("GetActivationAddress")]
        ValueTask<SiloAddress?> GetActivationAddress(IAddressable reference);

        [Alias("317D82B6")]
        ValueTask<SiloAddress?> GetActivationAddress(IAddressable reference, CancellationToken cancellationToken)
            => GetActivationAddress(reference);

        /// <summary>
        /// 返回指定 Grain 类型的所有激活实例。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns>指定类型的所有活动 Grain 的列表。</returns>
        [Alias("GetActiveGrains")]
        ValueTask<List<GrainId>> GetActiveGrains(GrainType type);

        [Alias("3DB7923B")]
        ValueTask<List<GrainId>> GetActiveGrains(GrainType type, CancellationToken cancellationToken)
            => GetActiveGrains(type);

        /// <summary>
        /// 获取指定主机上 Grain 调用频率的估计统计信息。
        /// </summary>
        /// <param name="hostsIds">要从中请求 Grain 调用频率计数的主机。</param>
        /// <returns>Grain 调用频率估计值的列表。</returns>
        /// <remarks>
        /// 注意，结果集合不一定包含所有 Grain 调用；它包含调用频率最高的调用的估计值。
        /// </remarks>
        [Alias("GetGrainCallFrequencies")]
        Task<List<GrainCallFrequency>> GetGrainCallFrequencies(SiloAddress[]? hostsIds = null);

        [Alias("0F06E027")]
        Task<List<GrainCallFrequency>> GetGrainCallFrequencies(
            SiloAddress[]? hostsIds,
            CancellationToken cancellationToken)
            => GetGrainCallFrequencies(hostsIds);

        /// <summary>
        /// 仅供测试使用。重置指定主机上的 Grain 调用频率计数。
        /// </summary>
        /// <param name="hostsIds">要执行该操作的主机。</param>
        /// <returns>表示所执行工作的任务。</returns>
        [Alias("ResetGrainCallFrequencies")]
        ValueTask ResetGrainCallFrequencies(SiloAddress[]? hostsIds = null);

        [Alias("54FE0FEC")]
        ValueTask ResetGrainCallFrequencies(SiloAddress[]? hostsIds, CancellationToken cancellationToken)
            => ResetGrainCallFrequencies(hostsIds);
    }

    /// <summary>
    /// 表示从源 Grain 到目标 Grain 的调用频率估计值。
    /// </summary>
    [GenerateSerializer]
    [Alias("Orleans.Runtime.GrainCallFrequency")]
    [Immutable]
    public struct GrainCallFrequency
    {
        /// <summary>
        /// 源 Grain。
        /// </summary>
        [Id(0)]
        public GrainId SourceGrain { get; set; }

        /// <summary>
        /// 目标 Grain。
        /// </summary>
        [Id(1)]
        public GrainId TargetGrain { get; set; }

        /// <summary>
        /// 源主机。
        /// </summary>
        [Id(2)]
        public SiloAddress? SourceHost { get; set; }

        /// <summary>
        /// 目标主机。
        /// </summary>
        [Id(3)]
        public SiloAddress? TargetHost { get; set; }

        /// <summary>
        /// 估计的调用次数。
        /// </summary>
        [Id(4)]
        public ulong CallCount { get; set; }
    }
}
