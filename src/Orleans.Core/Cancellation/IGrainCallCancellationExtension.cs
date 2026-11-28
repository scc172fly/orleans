using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;
using Orleans.Internal;

namespace Orleans.Runtime;

internal interface IGrainCallCancellationExtension : IGrainExtension
{
    /// <summary>
    /// 表示取消令牌已被取消。
    /// </summary>
    /// <param name="senderGrainId">
    /// 原始消息发送方的 <see cref="GrainId"/>。
    /// </param>
    /// <param name="messageId">
    /// 正在取消的请求消息的消息 ID。
    /// </param>
    /// <returns>
    /// 表示该操作的 <see cref="Task"/>。（译注：此方法实际返回 ValueTask。）
    /// </returns>
    [AlwaysInterleave, OneWay]
    [Alias("FA239824")]
    ValueTask CancelRequestAsync(GrainId senderGrainId, CorrelationId messageId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 用于取消 Grain 调用的功能。
/// </summary>
internal interface IGrainCallCancellationManager
{
    /// <summary>
    /// 尝试取消 Grain 调用。
    /// </summary>
    void SignalCancellation(SiloAddress? targetSilo, GrainId targetGrainId, GrainId sendingGrainId, CorrelationId messageId);
}

internal sealed class ExternalClientGrainCallCancellationManager(IInternalGrainFactory grainFactory) : IGrainCallCancellationManager
{
    public void SignalCancellation(SiloAddress? targetSilo, GrainId targetGrainId, GrainId sendingGrainId, CorrelationId messageId)
    {
        var targetGrain = grainFactory.GetGrain<IGrainCallCancellationExtension>(targetGrainId);
        targetGrain.CancelRequestAsync(sendingGrainId, messageId, CancellationToken.None).Ignore();
    }
}
