using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Runtime
{
    /// <summary>
    /// 用于访问 Grain 与激活实例状态的远程接口。
    /// </summary>
    internal interface ICatalog : ISystemTarget
    {
        /// <summary>
        /// 开始停用此 Silo 上的指定激活实例。
        /// </summary>
        /// <param name="activationAddresses">要停用的激活实例的精确地址。</param>
        /// <param name="reasonCode">停用原因代码。</param>
        /// <param name="reasonText">停用原因文本。</param>
        /// <param name="cancellationToken">在停用开始之前取消请求的令牌。</param>
        /// <returns>
        /// 一个任务，在每个匹配的激活实例都已进入停用状态且不再接受
        /// 应用程序消息后完成。停用回调、目录注销、释放和移除操作继续
        /// 异步进行。
        /// </returns>
        [Alias("C4A56D7C")]
        Task DeleteActivations(
            List<GrainAddress> activationAddresses,
            DeactivationReasonCode reasonCode,
            string reasonText,
            CancellationToken cancellationToken = default);
    }
}
