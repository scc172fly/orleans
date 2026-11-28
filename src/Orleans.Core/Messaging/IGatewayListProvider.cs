using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Messaging
{
    /// <summary>
    /// 提供 Orleans 网关信息的接口。
    /// </summary>
    /// <remarks>
    /// 提供程序实现支持取消的 Async 方法，以取消其底层操作。
    /// 默认实现通过取消调用方的等待并
    /// 观察不带令牌的操作最终完成情况来支持现有提供程序。
    /// </remarks>
    public interface IGatewayListProvider
    {
        /// <summary>
        /// 初始化提供程序，此方法将在所有其他方法之前调用。
        /// </summary>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        Task InitializeGatewayListProvider();

        /// <summary>
        /// 在开始网关发现之前初始化提供程序。
        /// </summary>
        /// <param name="cancellationToken">用于取消初始化的令牌。</param>
        /// <returns>表示初始化操作的任务。</returns>
        async Task InitializeGatewayListProviderAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = InitializeGatewayListProvider();
            operation.Ignore();
            await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 返回客户端可用于连接 Orleans 集群的网关（Silo）列表。
        /// URI 格式为“gwy.tcp://IP:port/Generation”。有关 URI 格式的更多详情，请参阅 Utils.ToGatewayUri 和 Utils.ToSiloAddress。
        /// </summary>
        /// <returns>网关终结点列表。</returns>
        Task<IList<Uri>> GetGateways();

        /// <summary>
        /// 获取客户端可用于连接集群的网关终结点。
        /// </summary>
        /// <param name="cancellationToken">用于取消网关发现的令牌。</param>
        /// <returns>网关终结点列表。</returns>
        async Task<IList<Uri>> GetGatewaysAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = GetGateways();
            operation.Ignore();
            return await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 获取两次刷新之间的时间间隔。
        /// </summary>
        TimeSpan MaxStaleness { get; }

        /// <summary>
        /// 获取一个值，指示此 IGatewayListProvider 是否会刷新返回的信息，还是始终返回相同的网关列表。
        /// </summary>
        [Obsolete("This attribute is no longer used and all providers are considered updatable")]
        bool IsUpdatable { get; }
    }
}
