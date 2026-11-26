using System;
using Orleans.Runtime;

namespace Orleans.Core.Internal
{
    /// <summary>
    /// 提供功能,用于输入和退出代码部分 grain 期间提出相同请求的请求 <see cref="RequestContext.ReentrancyId"/> 允许重新进入 grain.
    /// </summary>
    public interface ICallChainReentrantGrainContext
    {
        /// <summary>
        /// 标记代码部分在 grain 期间提出相同请求的请求 <see cref="RequestContext.ReentrancyId"/> 允许重新进入 grain.
        /// </summary>
        void OnEnterReentrantSection(Guid reentrancyId);

        /// <summary>
        /// 标记代码的结尾 grain 期间提出相同请求的请求 <see cref="RequestContext.ReentrancyId"/> 允许重新进入 grain.
        /// </summary>
        void OnExitReentrantSection(Guid reentrancyId);
    }
}
