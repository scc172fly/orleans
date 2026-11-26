using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;
using Orleans.Internal;

namespace Orleans.Runtime;

internal interface IGrainCallCancellationExtension : IGrainExtension
{
    /// <summary>
    /// 表示取消一个标记已被取消 。
    /// </summary>
    /// <param name="senderGrainId">
    /// 原始发件人的 <see cref="GrainId"/> 。
    /// </param>
    /// <param name="messageId">
    /// 请求信件的代号已取消 。
    /// </param>
    /// <returns>
    /// 代表行动的一个 <see cref="Task"/> 。
    /// </returns>
    [AlwaysInterleave, OneWay]
    [Alias("FA239824")]
    ValueTask CancelRequestAsync(GrainId senderGrainId, CorrelationId messageId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 取消 grain 通话功能。
/// </summary>
internal interface IGrainCallCancellationManager
{
    /// <summary>
    /// 试图取消 grain 呼叫 。
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
