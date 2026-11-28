using System;
using Orleans.Runtime;

namespace Orleans.Core.Internal
{
    /// <summary>
    /// 提供进入及退出 Grain 中代码区段的功能；在该区段内，具有相同 <see cref="RequestContext.ReentrancyId"/> 的请求可以重新进入此 Grain。
    /// </summary>
    public interface ICallChainReentrantGrainContext
    {
        /// <summary>
        /// 标记 Grain 中代码区段的起始位置；在该区段内，具有相同 <see cref="RequestContext.ReentrancyId"/> 的请求可以重新进入此 Grain。
        /// </summary>
        void OnEnterReentrantSection(Guid reentrancyId);

        /// <summary>
        /// 标记 Grain 中代码区段的结束位置；在该区段内，具有相同 <see cref="RequestContext.ReentrancyId"/> 的请求可以重新进入此 Grain。
        /// </summary>
        void OnExitReentrantSection(Guid reentrancyId);
    }
}
