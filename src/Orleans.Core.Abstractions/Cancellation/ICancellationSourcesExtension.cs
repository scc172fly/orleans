using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;

namespace Orleans.Runtime
{
    /// <summary>
    /// Grain 取消运行时使用此扩展向 Grain 传播取消通知。
    /// </summary>
    internal interface ICancellationSourcesExtension : IGrainExtension
    {
        /// <summary>
        /// 指示取消令牌已取消。
        /// </summary>
        /// <param name="tokenId">
        /// 令牌 ID。
        /// </param>
        /// <returns>
        /// 表示该操作的 <see cref="Task"/>。
        /// </returns>
        [AlwaysInterleave]
        [Alias("50F75C16")]
        Task CancelRemoteToken(Guid tokenId, CancellationToken cancellationToken = default);
    }
}
