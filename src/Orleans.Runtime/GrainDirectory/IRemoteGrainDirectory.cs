using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.GrainDirectory;

namespace Orleans.Runtime
{
    /// <summary>
    /// 用于管理分布式分区式 Grain-Silo-激活实例目录的每 Silo 系统接口。
    /// </summary>
    internal interface IRemoteGrainDirectory : ISystemTarget, IDhtGrainDirectory
    {
        /// <summary>
        /// 记录一批新的 Grain 激活实例。
        /// 此方法只应在移交期间通过远程调用执行。
        /// </summary>
        /// <param name="addresses">要注册的 Grain 地址。</param>
        /// <returns></returns>
        [Alias("CD06EAEE")]
        Task RegisterMany(List<GrainAddress> addresses, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取给定 Grain 列表的更新信息。
        /// 此方法只应通过远程调用执行，用于刷新目录缓存。
        /// </summary>
        /// <param name="grainAndETagList">Grain 及世代（版本）号的列表。世代号表示
        /// 当前方法调用方持有的激活实例列表的版本。</param>
        /// <returns>元组列表，每个元组包含 Grain、激活实例列表的世代号，以及激活实例列表。
        /// 如果调用方的世代号与目标方一致，则激活实例列表为 null。如果目标方没有
        /// 该 Grain 的信息，则返回世代计数器 -1（且激活实例列表为 null）。</returns>
        [Alias("7DF50601")]
        Task<List<AddressAndTag>> LookUpMany(
            List<(GrainId GrainId, int Version)> grainAndETagList,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 将拆分所得分区中的激活实例注册到此目录。
        /// </summary>
        /// <param name="singleActivations">来自拆分所得分区的单激活实例注册记录。</param>
        /// <returns></returns>
        [Alias("9ABE3793")]
        Task AcceptSplitPartition(List<GrainAddress> singleActivations, CancellationToken cancellationToken = default);
    }
}
