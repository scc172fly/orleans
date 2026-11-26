using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Providers;

namespace Orleans.Runtime
{
    /// <summary>
    /// silos系统管理功能的接口,
    /// 以 grain 形式暴露,用于接收远程请求/命令。
    /// </summary>
    public interface IManagementGrain : IGrainWithIntegerKey, IVersionManager
    {
        /// <summary>
        /// 禁用指定当前实例 grain 当前所有活动 silos.
        /// </summary>
        /// <remarks>
        /// 此操作从不激活 grain 或读取其持续状态。 它包括无国籍工人
        /// 并且当不存在实例时是无选项。它不妨碍随后的请求启动 grain。
        /// </remarks>
        /// <param name="grainId">准确 grain 身份,以便解除作用</param>
        /// <param name="cancellationToken">取消等待完成的取消。 已经请求的取消继续 。</param>
        /// <returns>匹配实例完成后完成的禁用任务。</returns>
        [Alias("DeactivateGrain")]
        Task DeactivateGrain(GrainId grainId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取当前已知的 silo 主机列表和状态。
        /// </summary>
        /// <param name="onlyActive">是否应该返回当前活动数据 silos ,
        /// 或默认数据,以显示所有当前和以往 silo 实例[包括加入状态或死状态中的情况]。</param>
        /// <returns>东道主和相应的地位</returns>
        [Alias("GetHosts")]
        Task<Dictionary<SiloAddress, SiloStatus>> GetHosts(bool onlyActive = false);

        [Alias("4C0864C2")]
        Task<Dictionary<SiloAddress, SiloStatus>> GetHosts(bool onlyActive, CancellationToken cancellationToken)
            => GetHosts(onlyActive);

        /// <summary>
        /// 获取 silo 主机列表和当前已知的本组中的会籍信息。
        /// </summary>
        /// <param name="onlyActive">是否应该返回当前活动数据 silos ,
        /// 或默认数据,以显示所有当前和以往 silo 实例[包括加入状态或死状态中的情况]。</param>
        /// <returns>主机条目。</returns>
        [Alias("GetDetailedHosts")]
        Task<MembershipEntry[]> GetDetailedHosts(bool onlyActive = false);

        [Alias("CC6CCBC3")]
        Task<MembershipEntry[]> GetDetailedHosts(bool onlyActive, CancellationToken cancellationToken)
            => GetDetailedHosts(onlyActive);

        /// <summary>
        /// 执行运行 .NET 指定垃圾收集器 silos.
        /// </summary>
        /// <param name="hostsIds">列表 silos 此命令要发送到 。</param>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        [Alias("ForceGarbageCollection")]
        Task ForceGarbageCollection(SiloAddress[] hostsIds);

        [Alias("5922EB76")]
        Task ForceGarbageCollection(SiloAddress[] hostsIds, CancellationToken cancellationToken)
            => ForceGarbageCollection(hostsIds);

        /// <summary>执行运行 Orleans 指定区域内的激活收藏器 silos.</summary>
        /// <param name="hostsIds">列表 silos 此命令要发送到 。</param>
        /// <param name="ageLimit">最大空闲时间将收集到激活的待收集时间。</param>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        [Alias("ForceActivationCollectionOnSilos")]
        Task ForceActivationCollection(SiloAddress[] hostsIds, TimeSpan ageLimit);

        [Alias("329F9A1B")]
        Task ForceActivationCollection(SiloAddress[] hostsIds, TimeSpan ageLimit, CancellationToken cancellationToken)
            => ForceActivationCollection(hostsIds, ageLimit);

        /// <summary>
        /// 部队启动集聚
        /// </summary>
        /// <param name="ageLimit">年龄限制。 Grains 闲置时间超过这一期限者有资格被收。</param>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        [Alias("ForceActivationCollection")]
        Task ForceActivationCollection(TimeSpan ageLimit);

        [Alias("54E6D1D1")]
        Task ForceActivationCollection(TimeSpan ageLimit, CancellationToken cancellationToken)
            => ForceActivationCollection(ageLimit);

        /// <summary>执行运行 silo 指定对象中的统计收集器 silos.</summary>
        /// <param name="siloAddresses">列表 silos 此命令要发送到 。</param>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        [Alias("ForceRuntimeStatisticsCollection")]
        Task ForceRuntimeStatisticsCollection(SiloAddress[] siloAddresses);

        [Alias("B761B345")]
        Task ForceRuntimeStatisticsCollection(SiloAddress[] siloAddresses, CancellationToken cancellationToken)
            => ForceRuntimeStatisticsCollection(siloAddresses);

        /// <summary>
        /// 最近一次返回 silo 指定时间统计信息 silos.
        /// </summary>
        /// <param name="hostsIds">列表 silos 此命令要发送到 。</param>
        /// <returns>指定主机的运行时统计。</returns>
        [Alias("GetRuntimeStatistics")]
        Task<SiloRuntimeStatistics[]> GetRuntimeStatistics(SiloAddress[] hostsIds);

        [Alias("2D761B36")]
        Task<SiloRuntimeStatistics[]> GetRuntimeStatistics(SiloAddress[] hostsIds, CancellationToken cancellationToken)
            => GetRuntimeStatistics(hostsIds);

        /// <summary>
        /// 返回最新的 grain 统计资料,该统计资料在 silos中合并。
        /// </summary>
        /// <param name="hostsIds">列表 silos 此命令要发送到 。</param>
        /// 指定主机的简单<returns> grain 统计。</returns>
        [Alias("GetSimpleGrainStatisticsOnSilos")]
        Task<SimpleGrainStatistic[]> GetSimpleGrainStatistics(SiloAddress[] hostsIds);

        [Alias("3CFF788C")]
        Task<SimpleGrainStatistic[]> GetSimpleGrainStatistics(SiloAddress[] hostsIds, CancellationToken cancellationToken)
            => GetSimpleGrainStatistics(hostsIds);

        /// <summary>
        /// 返回最新的 grain 统计资料,并综合所有 silos。
        /// </summary>
        /// <returns>简单 grain 统计。</returns>
        [Alias("GetSimpleGrainStatistics")]
        Task<SimpleGrainStatistic[]> GetSimpleGrainStatistics();

        [Alias("ACCE9D6A")]
        Task<SimpleGrainStatistic[]> GetSimpleGrainStatistics(CancellationToken cancellationToken)
            => GetSimpleGrainStatistics();

        /// <summary>
        /// 返回最近详细的 grain 统计信息,该信息在指定类型中以 silos 组合。
        /// </summary>
        /// <param name="hostsIds">列表 silos 此命令要发送到 。</param>
        /// <param name="types">阵列数 grain 将选择类型以</param>
        /// <returns>详细明细 grain 统计。</returns>
        [Alias("GetDetailedGrainStatistics")]
        Task<DetailedGrainStatistic[]> GetDetailedGrainStatistics(string[]? types = null, SiloAddress[]? hostsIds = null);

        [Alias("0A1C0D82")]
        Task<DetailedGrainStatistic[]> GetDetailedGrainStatistics(
            string[]? types,
            SiloAddress[]? hostsIds,
            CancellationToken cancellationToken)
            => GetDetailedGrainStatistics(types, hostsIds);
        /// <summary>
        /// 得到 grain 特定激活计数 grain 类型。
        /// </summary>
        /// <param name="grainReference">下 grain 参考。</param>
        /// <returns>获得启动次数 grains 具有与所提供的相同类型 grain 参考。</returns>
        [Alias("GetGrainActivationCount")]
        Task<int> GetGrainActivationCount(GrainReference grainReference);

        [Alias("AEDE93F6")]
        Task<int> GetGrainActivationCount(GrainReference grainReference, CancellationToken cancellationToken)
            => GetGrainActivationCount(grainReference);
        /// <summary>
        /// 返回全部当前数的总数 grain 在所有 silos.
        /// </summary>
        /// <returns>总数总数 grain 在所有 silos.</returns>
        [Alias("GetTotalActivationCount")]
        Task<int> GetTotalActivationCount();

        [Alias("D7365B43")]
        Task<int> GetTotalActivationCount(CancellationToken cancellationToken)
            => GetTotalActivationCount();

        /// <summary>
        /// 对组内的所有 silos 指定提供者执行控制命令。
        /// 向每个 silo 所有已知的提供者发送命令,这些提供者既符合 <c>providerTypeFullName</c> 参数,也符合 <c>providerName</c> 参数。
        /// </summary>
        /// <remarks>
        /// 供应商必须执行 <c>Orleans.Providers.IControllable</c>
        /// 以接收这些控制频道命令。
        /// </remarks>
        /// <param name="providerName">要发送此命令到的提供者名 。</param>
        /// <param name="command">此命令的 ID / 序号 。
        /// 这是对 Orleans 运行时间的不透明值 - 控制协议语义由发送者和提供者决定。</param>
        /// <param name="arg">一个不透明的命令参数 。
        /// 这是对 Orleans 运行时间的不透明值 - 控制协议语义由发送者和提供者决定。</param>
        /// <returns>这一行动的完成承诺。</returns>
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
        /// 返回 <see cref="Orleans.Runtime.SiloAddress"/> 特定 Grain 激活( 如果有的话) 。
        /// </summary>
        /// <remarks>
        /// 请注意,这种方法并不代表对 Grain 目录的强烈一致看法。
        /// 返回这种方法时,所依据的是 grain 最后已知的状态,该状态在打电话者收到请求之前可能或可能没有更新。
        /// </remarks>
        /// <param name="reference">下 <see cref="Orleans.Runtime.IAddressable"/> 抬头看</param>
        /// <returns>下 <see cref="Orleans.Runtime.SiloAddress"/> 位于 Grain 或未激活的,从该物质最后已知状态的快照中拍摄 Grain 分类表。</returns>
        [Alias("GetActivationAddress")]
        ValueTask<SiloAddress?> GetActivationAddress(IAddressable reference);

        [Alias("317D82B6")]
        ValueTask<SiloAddress?> GetActivationAddress(IAddressable reference, CancellationToken cancellationToken)
            => GetActivationAddress(reference);

        /// <summary>
        /// 返回指定 grain 类型的所有激活。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns>全部活动列表 grains 指定类型。</returns>
        [Alias("GetActiveGrains")]
        ValueTask<List<GrainId>> GetActiveGrains(GrainType type);

        [Alias("3DB7923B")]
        ValueTask<List<GrainId>> GetActiveGrains(GrainType type, CancellationToken cancellationToken)
            => GetActiveGrains(type);

        /// <summary>
        /// 从指定的主机获取估计 grain 调用频率统计。
        /// </summary>
        /// <param name="hostsIds">请求 grain 调用频率计数的主机。</param>
        /// <returns>估计估计数清单 grain 呼叫频率</returns>
        /// <remarks>
        /// 请注意,由此收集的电话不一定包含所有 grain 通话,其中含有对最高频率通话的估计。
        /// </remarks>
        [Alias("GetGrainCallFrequencies")]
        Task<List<GrainCallFrequency>> GetGrainCallFrequencies(SiloAddress[]? hostsIds = null);

        [Alias("0F06E027")]
        Task<List<GrainCallFrequency>> GetGrainCallFrequencies(
            SiloAddress[]? hostsIds,
            CancellationToken cancellationToken)
            => GetGrainCallFrequencies(hostsIds);

        /// <summary>
        /// 仅用于测试。 重新选择 grain 呼叫频率会根据指定的主机计算 。
        /// </summary>
        /// <param name="hostsIds">请求主机启动操作。</param>
        /// <returns>一项代表所完成的工作的任务。</returns>
        [Alias("ResetGrainCallFrequencies")]
        ValueTask ResetGrainCallFrequencies(SiloAddress[]? hostsIds = null);

        [Alias("54FE0FEC")]
        ValueTask ResetGrainCallFrequencies(SiloAddress[]? hostsIds, CancellationToken cancellationToken)
            => ResetGrainCallFrequencies(hostsIds);
    }

    /// <summary>
    /// 表示从源 grain 呼叫目标 grain的频率电话的估计。
    /// </summary>
    [GenerateSerializer]
    [Alias("Orleans.Runtime.GrainCallFrequency")]
    [Immutable]
    public struct GrainCallFrequency
    {
        /// <summary>
        /// 源 grain。
        /// </summary>
        [Id(0)]
        public GrainId SourceGrain { get; set; }

        /// <summary>
        /// 目标 grain。
        /// </summary>
        [Id(1)]
        public GrainId TargetGrain { get; set; }

        /// <summary>
        /// 源主机 。
        /// </summary>
        [Id(2)]
        public SiloAddress? SourceHost { get; set; }

        /// <summary>
        /// 目标主机
        /// </summary>
        [Id(3)]
        public SiloAddress? TargetHost { get; set; }

        /// <summary>
        /// 电话的估计次数。
        /// </summary>
        [Id(4)]
        public ulong CallCount { get; set; }
    }
}
