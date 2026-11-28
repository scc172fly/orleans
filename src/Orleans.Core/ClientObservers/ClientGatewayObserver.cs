using System.Threading;
using Orleans.Messaging;
using Orleans.Runtime;

namespace Orleans.ClientObservers
{
    /// <summary>
    /// 处理发送给已连接客户端的网关通知。
    /// </summary>
    internal interface IClientGatewayObserver : IGrainObserver
    {
        /// <summary>
        /// 通知客户端停止向指定网关发送消息。
        /// </summary>
        /// <param name="gateway">网关。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        [Alias("AFB768FD")]
        void StopSendingToGateway(SiloAddress gateway, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 处理网关通知事件。
    /// </summary>
    internal sealed class ClientGatewayObserver : ClientObserver, IClientGatewayObserver
    {
        private static readonly IdSpan ScopedId = IdSpan.Create(nameof(ClientGatewayObserver));

        private readonly GatewayManager gatewayManager;

        /// <summary>
        /// 初始化 <see cref="ClientGatewayObserver"/> 类的新实例。
        /// </summary>
        /// <param name="gatewayManager">
        /// 网关管理器。
        /// </param>
        public ClientGatewayObserver(GatewayManager gatewayManager)
        {
            this.gatewayManager = gatewayManager;
        }

        /// <inheritdoc />
        public void StopSendingToGateway(SiloAddress gateway, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.gatewayManager.MarkAsUnavailableForSend(gateway);
        }

        internal override ObserverGrainId GetObserverGrainId(ClientGrainId clientId) => ObserverGrainId.Create(clientId, ScopedId);

        internal static IClientGatewayObserver GetObserver(IInternalGrainFactory grainFactory, ClientGrainId clientId)
        {
            var observerId = ObserverGrainId.Create(clientId, ScopedId);
            return grainFactory.GetGrain<IClientGatewayObserver>(observerId.GrainId);
        }
    }
}
