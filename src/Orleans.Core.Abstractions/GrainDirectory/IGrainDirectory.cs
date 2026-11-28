using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.GrainDirectory
{
    /// <summary>
    /// Grain 目录实现的接口。
    /// </summary>
    public interface IGrainDirectory
    {
        /// <summary>
        /// 在目录中注册一个 <see cref="GrainAddress"/> 条目。
        /// 对于 <see cref="GrainAddress"/> 注册记录，每个 <see cref="GrainAddress.GrainId"/> 最多只能对应一条。如果已有
        /// 现有条目，目录不会覆盖该条目。
        /// </summary>
        /// <param name="address">要注册的 <see cref="GrainAddress"/>。</param>
        /// <returns>在目录中实际注册的 <see cref="GrainAddress"/>。</returns>
        Task<GrainAddress?> Register(GrainAddress address);

        /// <summary>
        /// 在目录中注册一个 <see cref="GrainAddress"/> 条目。
        /// </summary>
        /// <param name="address">要注册的 <see cref="GrainAddress"/>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>在目录中实际注册的 <see cref="GrainAddress"/>。</returns>
        Task<GrainAddress?> Register(GrainAddress address, CancellationToken cancellationToken) =>
            Register(address).WaitAsync(cancellationToken);

        /// <summary>
        /// 在目录中注册一个 <see cref="GrainAddress"/> 条目。
        /// 对于 <see cref="GrainAddress"/> 注册记录，每个 <see cref="GrainAddress.GrainId"/> 最多只能对应一条。如果已有
        /// 现有条目，目录不会覆盖该条目。
        /// </summary>
        /// <param name="address">要注册的 <see cref="GrainAddress"/>。</param>
        /// <param name="previousAddress">要替换的上一条注册记录（如果有）。</param>
        /// <returns>在目录中实际注册的 <see cref="GrainAddress"/>。</returns>
        Task<GrainAddress?> Register(GrainAddress address, GrainAddress? previousAddress) => GrainDirectoryExtension.Register(this, address, previousAddress);

        /// <summary>
        /// 在目录中注册一个 <see cref="GrainAddress"/> 条目。
        /// </summary>
        /// <param name="address">要注册的 <see cref="GrainAddress"/>。</param>
        /// <param name="previousAddress">上一条注册记录（如果有）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>在目录中实际注册的 <see cref="GrainAddress"/>。</returns>
        Task<GrainAddress?> Register(GrainAddress address, GrainAddress? previousAddress, CancellationToken cancellationToken) =>
            Register(address, previousAddress).WaitAsync(cancellationToken);

        /// <summary>
        /// 从目录注销指定的 <see cref="GrainAddress"/> 条目。
        /// </summary>
        /// <param name="address">
        /// 要注销的 <see cref="GrainAddress"/>。
        /// </param>
        /// <returns>
        /// 表示该操作的 <see cref="Task"/>。
        /// </returns>
        Task Unregister(GrainAddress address);

        /// <summary>
        /// 从目录注销指定的 <see cref="GrainAddress"/> 条目。
        /// </summary>
        /// <param name="address">要注销的 <see cref="GrainAddress"/>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Unregister(GrainAddress address, CancellationToken cancellationToken) =>
            Unregister(address).WaitAsync(cancellationToken);

        /// <summary>
        /// 根据指定 Grain ID 查找 <see cref="GrainAddress"/>。
        /// </summary>
        /// <param name="grainId">要查找的 Grain ID。</param>
        /// <returns>在目录中找到的 <see cref="GrainAddress"/> 条目（如果有）。</returns>
        Task<GrainAddress?> Lookup(GrainId grainId);

        /// <summary>
        /// 根据指定 Grain ID 查找 <see cref="GrainAddress"/>。
        /// </summary>
        /// <param name="grainId">要查找的 Grain ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>在目录中找到的 <see cref="GrainAddress"/> 条目（如果有）。</returns>
        Task<GrainAddress?> Lookup(GrainId grainId, CancellationToken cancellationToken) =>
            Lookup(grainId).WaitAsync(cancellationToken);

        /// <summary>
        /// 注销指向任一指定 Silo 的所有 Grain 目录条目。
        /// </summary>
        /// <remarks>
        /// 视具体实现而定，此操作可能不执行任何动作。
        /// </remarks>
        /// <param name="siloAddresses">要从目录中移除的 Silo。</param>
        /// <returns>
        /// 表示该操作的 <see cref="Task"/>。
        /// </returns>
        Task UnregisterSilos(List<SiloAddress> siloAddresses);

        /// <summary>
        /// 注销指向任一指定 Silo 的所有 Grain 目录条目。
        /// </summary>
        /// <param name="siloAddresses">要从目录中移除的 Silo。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
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
