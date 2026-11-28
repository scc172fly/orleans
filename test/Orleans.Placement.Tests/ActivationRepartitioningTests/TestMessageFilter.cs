using Orleans.Runtime.Placement;
using Orleans.Runtime.Placement.Repartitioning;

namespace UnitTests.ActivationRepartitioningTests;

/// <summary>
/// 忽略客户端消息，以简化测试。
/// </summary>
internal sealed class TestMessageFilter(GrainMigratabilityChecker checker) : IRepartitionerMessageFilter
{
    private readonly RepartitionerMessageFilter _messageFilter = new(checker);

    public bool IsAcceptable(Message message, out bool isSenderMigratable, out bool isTargetMigratable) =>
        _messageFilter.IsAcceptable(message, out isSenderMigratable, out isTargetMigratable) &&
        !message.SendingGrain.IsClient() && !message.TargetGrain.IsClient();
}
