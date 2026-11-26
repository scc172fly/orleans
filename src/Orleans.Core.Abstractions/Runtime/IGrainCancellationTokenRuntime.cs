using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Runtime
{
    /// <summary>
    /// <see cref="GrainCancellationToken"/> 和 <see cref="GrainCancellationTokenSource"/>要求的功能性。
    /// </summary>
    internal interface IGrainCancellationTokenRuntime
    {
        /// <summary>
        /// 以所提供的代号取消 <see cref="GrainCancellationToken"/> 。
        /// </summary>
        /// <param name="id">下 grain 取消代号 ID 。</param>
        /// <param name="tokenSource">取消的 grain 符号源已被取消。</param>
        /// <param name="grainReferences">正在观察取消符号的 grain 引用。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Cancel(Guid id, CancellationTokenSource tokenSource, ConcurrentDictionary<GrainId, GrainReference> grainReferences);
    }
}
