using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Placement;
using Orleans.Runtime.Placement;

namespace Orleans.Runtime;

/// <summary>
/// 将排队的启动请求与启动一起迁移到该启动。
/// </summary>
internal sealed partial class ActivationData
{
    /// <summary>
    /// 是否因为它 silo 关闭。
    /// </summary>
    /// <remarks>
    /// 下 silo 使用以下 <see cref="DeactivationReasonCode.ShuttingDown"/> 当它关闭的时候,
    /// 一项由于任何其他原因正在解除活动的激活,将它的请求发回或拒绝。
    /// </remarks>
    private bool MigratesOnRetirement()
        => DeactivationReason.ReasonCode is DeactivationReasonCode.ShuttingDown
            && IsUsingGrainDirectory
            && _shared.Runtime.ServiceProvider.GetService<GrainMigratabilityChecker>() is { } checker
            && checker.IsMigratable(GrainId.Type, ImmovableKind.Any);

    /// <summary>
    /// 删除此激活后可以迁移的队列请求 。
    /// </summary>
    /// <returns>(a) 请求,或 <see langword="null"/> 如果无。</returns>
    private List<Message>? TakeRequestsForMigration()
    {
        var maxForwardCount = _shared.MessagingOptions.MaxForwardCount;
        List<Message>? requests = null;
        List<Message>? expired = null;
        lock (_lock)
        {
            _waitingRequests.RemoveAll(entry =>
            {
                // 仅允许本地信息无法逃避激活 。
                // 移徙算作向前推进,因此已用尽前方请求仍排队等待并被拒绝。
                if (entry.Message.IsLocalOnly || entry.Message.ForwardCount >= maxForwardCount)
                {
                    return false;
                }

                // 过期请求被撤销,因为收到时已经失效。
                if (entry.Message.IsExpired)
                {
                    (expired ??= []).Add(entry.Message);
                }
                else
                {
                    (requests ??= []).Add(entry.Message);
                }

                return true;
            });
        }

        if (expired is not null)
        {
            foreach (var message in expired)
            {
                _shared.InternalRuntime.MessagingTrace.OnDropExpiredMessage(message, MessagingInstruments.Phase.Dispatch);
                message.Dispose();
            }
        }

        return requests;
    }
}
