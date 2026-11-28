using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Runtime
{
    /// <summary>
    /// <see cref="GrainCancellationToken"/> 和 <see cref="GrainCancellationTokenSource"/> 所需的功能。
    /// </summary>
    internal interface IGrainCancellationTokenRuntime
    {
        /// <summary>
        /// 取消指定 ID 的 <see cref="GrainCancellationToken"/>。
        /// </summary>
        /// <param name="id">Grain 取消令牌 ID。</param>
        /// <param name="tokenSource">正在取消的 Grain 取消令牌源。</param>
        /// <param name="grainReferences">正在观察取消令牌的 Grain 引用。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Cancel(Guid id, CancellationTokenSource tokenSource, ConcurrentDictionary<GrainId, GrainReference> grainReferences);
    }
}
