using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Core.Internal
{
    /// <summary>
    /// 提供功能,用于在启动 grain 时开展管理业务。
    /// </summary>
    public interface IGrainManagementExtension : IGrainExtension
    {
        /// <summary>
        /// 当当前实例闲置时, 即将其禁用 。
        /// </summary>
        /// <returns>A <see cref="Task"/> ,代表方法调用。</returns>
        [Alias("DeactivateOnIdle")]
        ValueTask DeactivateOnIdle();

        /// <summary>
        /// 当当前实例闲置时, 即将其禁用 。
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> ,代表方法调用。</returns>
        [Alias("1B9614D1")]
        ValueTask DeactivateOnIdle(CancellationToken cancellationToken) => DeactivateOnIdle();

        /// <summary>
        /// 试图将当前实例在闲置后迁移到新地点。
        /// </summary>
        /// <returns>A <see cref="Task"/> ,代表方法调用。</returns>
        [Alias("MigrateOnIdle")]
        ValueTask MigrateOnIdle();

        /// <summary>
        /// 试图将当前实例在闲置后迁移到新地点。
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> ,代表方法调用。</returns>
        [Alias("4CC93B45")]
        ValueTask MigrateOnIdle(CancellationToken cancellationToken) => MigrateOnIdle();
    }
}
