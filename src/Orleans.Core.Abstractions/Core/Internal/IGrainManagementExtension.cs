using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Core.Internal
{
    /// <summary>
    /// 提供对 Grain 激活实例执行管理操作的功能。
    /// </summary>
    public interface IGrainManagementExtension : IGrainExtension
    {
        /// <summary>
        /// 当当前实例进入空闲状态时停用它。
        /// </summary>
        /// <returns>表示方法调用的 <see cref="Task"/>。</returns>
        [Alias("DeactivateOnIdle")]
        ValueTask DeactivateOnIdle();

        /// <summary>
        /// 当当前实例进入空闲状态时停用它。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示方法调用的 <see cref="Task"/>。</returns>
        [Alias("1B9614D1")]
        ValueTask DeactivateOnIdle(CancellationToken cancellationToken) => DeactivateOnIdle();

        /// <summary>
        /// 当前实例进入空闲状态后，尝试将其迁移到新位置。
        /// </summary>
        /// <returns>表示方法调用的 <see cref="Task"/>。</returns>
        [Alias("MigrateOnIdle")]
        ValueTask MigrateOnIdle();

        /// <summary>
        /// 当前实例进入空闲状态后，尝试将其迁移到新位置。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示方法调用的 <see cref="Task"/>。</returns>
        [Alias("4CC93B45")]
        ValueTask MigrateOnIdle(CancellationToken cancellationToken) => MigrateOnIdle();
    }
}
