using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Placement;
using Orleans.Runtime.Placement;

namespace Orleans.Runtime;

/// <summary>
/// 将此激活实例中排队的请求与激活实例一起迁移。
/// </summary>
internal sealed partial class ActivationData
{
    /// <summary>
    /// 获取此激活实例是否因所在 Silo 正在关闭而迁移。
    /// </summary>
    /// <remarks>
    /// Silo 在关闭时使用 <see cref="DeactivationReasonCode.ShuttingDown"/> 停用其激活实例。
    /// 因其他任何原因停用的激活实例会转发或拒绝其请求。
    /// </remarks>
    private bool MigratesOnRetirement()
        => DeactivationReason.ReasonCode is DeactivationReasonCode.ShuttingDown
            && IsUsingGrainDirectory
            && _shared.Runtime.ServiceProvider.GetService<GrainMigratabilityChecker>() is { } checker
            && checker.IsMigratable(GrainId.Type, ImmovableKind.Any);

    /// <summary>
    /// 移除可随此激活实例一起迁移的排队请求。
    /// </summary>
    /// <returns>请求集合；如果没有请求，则为 <see langword="null"/>。</returns>
    private List<Message>? TakeRequestsForMigration()
    {
        var maxForwardCount = _shared.MessagingOptions.MaxForwardCount;
        List<Message>? requests = null;
        List<Message>? expired = null;
        lock (_lock)
        {
            _waitingRequests.RemoveAll(entry =>
            {
                // 仅限本地的消息不得离开此激活实例。
                // 迁移计为一次转发，因此已耗尽转发次数的请求仍留在队列中，并被拒绝。
                if (entry.Message.IsLocalOnly || entry.Message.ForwardCount >= maxForwardCount)
                {
                    return false;
                }

                // 与接收时的处理相同，丢弃已过期的请求，因为其调用方已经超时。
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
