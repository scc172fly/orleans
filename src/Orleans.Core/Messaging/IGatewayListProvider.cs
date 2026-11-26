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
    /// 供应商采用取消通知的Async方法来取消其基本业务。
    /// 默认执行支持现有提供者, 取消调用者的等待和
    /// 观测无价操作完成情况。
    /// </remarks>
    public interface IGatewayListProvider
    {
        /// <summary>
        /// 初始化提供商, 将被召集到所有其他方法之前 。
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        Task InitializeGatewayListProvider();

        /// <summary>
        /// 在网关发现开始之前初始化提供者。
        /// </summary>
        /// <param name="cancellationToken">取消初始化所用的标记。</param>
        /// <returns>一项代表初始化操作的任务。</returns>
        async Task InitializeGatewayListProviderAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = InitializeGatewayListProvider();
            operation.Ignore();
            await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 返回网关列表( 返回) (n)silos客户端可以连接到 Orleans 组。
        /// 尤里语的形式是: "gwy.tcp/IP:port/Generation. " 关于尤里格式的更多详情,见 Utils.ToGatewayUri 和 Utils.ToSiloAddress 。
        /// </summary>
        /// <returns>网关端点列表。</returns>
        Task<IList<Uri>> GetGateways();

        /// <summary>
        /// 获取客户可以连接到集群的网关端点。
        /// </summary>
        /// <param name="cancellationToken">用于取消网关发现时使用的标记。</param>
        /// <returns>网关端点列表。</returns>
        async Task<IList<Uri>> GetGatewaysAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = GetGateways();
            operation.Ignore();
            return await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 可以在休息之间得到时间间隔。
        /// </summary>
        TimeSpan MaxStaleness { get; }

        /// <summary>
        /// 获得一个值,显示该 IGatewayListProvider 是否刷新了它返回的信息,还是总是返回相同的网关列表。
        /// </summary>
        [Obsolete("This attribute is no longer used and all providers are considered updatable")]
        bool IsUpdatable { get; }
    }
}
