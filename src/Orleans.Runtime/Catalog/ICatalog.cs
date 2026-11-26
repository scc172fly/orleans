using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Runtime
{
    /// <summary>
    /// 远程接口到 grain 和激活状态
    /// </summary>
    internal interface ICatalog : ISystemTarget
    {
        /// <summary>
        /// 开始取消此 silo上的指定激活。
        /// </summary>
        /// <param name="activationAddresses">确切的激活地址为停止运行。</param>
        /// <param name="reasonCode">停止使用的理由代码。</param>
        /// <param name="reasonText">停止使用的理由文本。</param>
        /// <param name="cancellationToken">解除功能开始前取消请求的符号。</param>
        /// <returns>
        /// 任务, 任务在每次匹配激活后完成, 任务已进入停用且不再接受
        /// 继续取消活动回召、目录解登记、处置和清除
        /// 毫无节制
        /// </returns>
        [Alias("C4A56D7C")]
        Task DeleteActivations(
            List<GrainAddress> activationAddresses,
            DeactivationReasonCode reasonCode,
            string reasonText,
            CancellationToken cancellationToken = default);
    }
}
