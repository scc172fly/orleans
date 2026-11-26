using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.GrainDirectory
{
    /// <summary>
    /// grain 目录实施接口
    /// </summary>
    public interface IGrainDirectory
    {
        /// <summary>
        /// 在目录中注册 <see cref="GrainAddress"/> 条目。
        /// 只有一个 <see cref="GrainAddress"/> 百分比/ 百 <see cref="GrainAddress.GrainId"/> 中已经注册的,如果已经登记
        /// 已有条目, 目录不会覆盖它 。
        /// </summary>
        /// <param name="address">下 <see cref="GrainAddress"/> 登记</param>
        /// <returns>下 <see cref="GrainAddress"/> 已在目录中有效注册。</returns>
        Task<GrainAddress?> Register(GrainAddress address);

        /// <summary>
        /// 在目录中注册 <see cref="GrainAddress"/> 条目。
        /// </summary>
        /// <param name="address">下 <see cref="GrainAddress"/> 登记。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>下 <see cref="GrainAddress"/> 已在目录中有效注册。</returns>
        Task<GrainAddress?> Register(GrainAddress address, CancellationToken cancellationToken) =>
            Register(address).WaitAsync(cancellationToken);

        /// <summary>
        /// 在目录中注册 <see cref="GrainAddress"/> 条目。
        /// 只有一个 <see cref="GrainAddress"/> 百分比/ 百 <see cref="GrainAddress.GrainId"/> 中已经注册的,如果已经登记
        /// 已有条目, 目录不会覆盖它 。
        /// </summary>
        /// <param name="address">下 <see cref="GrainAddress"/> 登记</param>
        /// <param name="previousAddress">先前的登记将替换(如果有的话)</param>
        /// <returns>下 <see cref="GrainAddress"/> 已在目录中有效注册。</returns>
        Task<GrainAddress?> Register(GrainAddress address, GrainAddress? previousAddress) => GrainDirectoryExtension.Register(this, address, previousAddress);

        /// <summary>
        /// 在目录中注册 <see cref="GrainAddress"/> 条目。
        /// </summary>
        /// <param name="address">下 <see cref="GrainAddress"/> 登记。</param>
        /// <param name="previousAddress">先前的登记(如有的话)</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>下 <see cref="GrainAddress"/> 已在目录中有效注册。</returns>
        Task<GrainAddress?> Register(GrainAddress address, GrainAddress? previousAddress, CancellationToken cancellationToken) =>
            Register(address, previousAddress).WaitAsync(cancellationToken);

        /// <summary>
        /// 取消从目录中输入指定的 <see cref="GrainAddress"/> 条目的登记。
        /// </summary>
        /// <param name="address">
        /// 解除登记所需的 <see cref="GrainAddress"/> 。
        /// </param>
        /// <returns>
        /// 代表行动的一个 <see cref="Task"/> 。
        /// </returns>
        Task Unregister(GrainAddress address);

        /// <summary>
        /// 取消从目录中输入指定的 <see cref="GrainAddress"/> 条目的登记。
        /// </summary>
        /// <param name="address">解除登记所需的 <see cref="GrainAddress"/> 。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Unregister(GrainAddress address, CancellationToken cancellationToken) =>
            Unregister(address).WaitAsync(cancellationToken);

        /// <summary>
        /// 查找 <see cref="GrainAddress"/> 给定 Grain ID.
        /// </summary>
        /// <param name="grainId">要查找的 Grain ID</param>
        /// <returns>下 <see cref="GrainAddress"/> 在目录中找到的条目,如果</returns>
        Task<GrainAddress?> Lookup(GrainId grainId);

        /// <summary>
        /// 查找 <see cref="GrainAddress"/> 给定 Grain ID.
        /// </summary>
        /// <param name="grainId">要查找的 Grain ID。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>目录中找到的 <see cref="GrainAddress"/> 条目,如果有的话。</returns>
        Task<GrainAddress?> Lookup(GrainId grainId, CancellationToken cancellationToken) =>
            Lookup(grainId).WaitAsync(cancellationToken);

        /// <summary>
        /// 全部不登记者 grain 指任何指定条目的目录条目 silos.
        /// </summary>
        /// <remarks>
        /// 视执行情况而定,可以是无行动。
        /// </remarks>
        /// <param name="siloAddresses">将从目录中删除的 silos</param>
        /// <returns>
        /// 代表行动的一个 <see cref="Task"/> 。
        /// </returns>
        Task UnregisterSilos(List<SiloAddress> siloAddresses);

        /// <summary>
        /// 全部不登记者 grain 指任何指定条目的目录条目 silos.
        /// </summary>
        /// <param name="siloAddresses">将从目录中删除的 silos 。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task UnregisterSilos(List<SiloAddress> siloAddresses, CancellationToken cancellationToken) =>
            UnregisterSilos(siloAddresses).WaitAsync(cancellationToken);
    }

    internal static class GrainDirectoryExtension
    {
        internal static async Task<GrainAddress?> Register(IGrainDirectory directory, GrainAddress address, GrainAddress? previousAddress)
        {
            if (previousAddress is not null)
            {
                await directory.Unregister(previousAddress);
            }

            return await directory.Register(address);
        }
    }
}
