using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;

namespace Orleans.Runtime
{
    /// <summary>
    /// grain 取消运行时用于向 grains发布注销通知的扩展时间。
    /// </summary>
    internal interface ICancellationSourcesExtension : IGrainExtension
    {
        /// <summary>
        /// 表示取消一个标记已被取消 。
        /// </summary>
        /// <param name="tokenId">
        /// 象征性id。
        /// </param>
        /// <returns>
        /// 代表行动的一个 <see cref="Task"/> 。
        /// </returns>
        [AlwaysInterleave]
        [Alias("50F75C16")]
        Task CancelRemoteToken(Guid tokenId, CancellationToken cancellationToken = default);
    }
}
