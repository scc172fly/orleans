using System.Threading;
using System.Threading.Tasks;
using Orleans.EventSourcing;
using Orleans.Runtime;

namespace Orleans.SystemTargetInterfaces
{
    /// <summary>
    /// 协议网关充当中继，将来自其他集群的入站协议消息
    /// 转发给此集群中的相应 Grain。
    /// </summary>
    internal interface ILogConsistencyProtocolGateway : ISystemTarget
    {
        [Alias("C86A1066")]
        Task<ILogConsistencyProtocolMessage?> RelayMessage(GrainId id, ILogConsistencyProtocolMessage payload, CancellationToken cancellationToken = default);
    }
}
