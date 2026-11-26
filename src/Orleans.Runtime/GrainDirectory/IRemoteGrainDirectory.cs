using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.GrainDirectory;

namespace Orleans.Runtime
{
    /// <summary>
    /// 人均 --silo 用于管理分布式、分割式 grain-silo- 激活目录
    /// </summary>
    internal interface IRemoteGrainDirectory : ISystemTarget, IDhtGrainDirectory
    {
        /// <summary>
        /// 记录一连串新的 grain 激活。
        /// 这种方法只应在交接时远程调用。
        /// </summary>
        /// <param name="addresses">发 言 的 国 址 grains 登记</param>
        /// <returns></returns>
        [Alias("CD06EAEE")]
        Task RegisterMany(List<GrainAddress> addresses, CancellationToken cancellationToken = default);

        /// <summary>
        /// 在给定的 grains列表上获取更新信息。
        /// 此方法应仅远程调用以刷新目录缓存 。
        /// </summary>
        /// <param name="grainAndETagList">列表中的 grains 和(转换)数字。 
        /// 使用该方法的援引者目前持有的激活清单。</param>
        /// <returns>持有 grain的图例清单,启动清单的生成数和激活清单。 
        /// 如果援引者的相数与目的地编号相符,则该列表为无效。如果目标不
        /// 保留 grain上的信息, 将返回生成计 -1 (启动列表无效)</returns>
        [Alias("7DF50601")]
        Task<List<AddressAndTag>> LookUpMany(
            List<(GrainId GrainId, int Version)> grainAndETagList,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 注册此目录的分割分区激活 。
        /// </summary>
        /// <param name="singleActivations">分割分割区单激活登记。</param>
        /// <returns></returns>
        [Alias("9ABE3793")]
        Task AcceptSplitPartition(List<GrainAddress> singleActivations, CancellationToken cancellationToken = default);
    }
}
