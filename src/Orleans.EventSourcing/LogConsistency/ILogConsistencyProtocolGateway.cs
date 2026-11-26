using System.Threading;
using System.Threading.Tasks;
using Orleans.EventSourcing;
using Orleans.Runtime;

namespace Orleans.SystemTargetInterfaces
{
    /// <summary>
    /// 协议网关是一个转发从其他组群发送接收到的协议信息的中继器
    /// 与本组中适当的 grain 相关。
    /// </summary>
    internal interface ILogConsistencyProtocolGateway : ISystemTarget
    {
        [Alias("C86A1066")]
        Task<ILogConsistencyProtocolMessage?> RelayMessage(GrainId id, ILogConsistencyProtocolMessage payload, CancellationToken cancellationToken = default);
    }
}
