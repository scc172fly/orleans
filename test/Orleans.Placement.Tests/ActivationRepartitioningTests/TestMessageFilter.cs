using Orleans.Runtime.Placement;
using Orleans.Runtime.Placement.Repartitioning;

namespace UnitTests.ActivationRepartitioningTests;

/// <summary>
/// 忽略客户端信息,使测试更容易
/// </summary>
internal sealed class TestMessageFilter(GrainMigratabilityChecker checker) : IRepartitionerMessageFilter
{
    private readonly RepartitionerMessageFilter _messageFilter = new(checker);

    public bool IsAcceptable(Message message, out bool isSenderMigratable, out bool isTargetMigratable) =>
        _messageFilter.IsAcceptable(message, out isSenderMigratable, out isTargetMigratable) &&
        !message.SendingGrain.IsClient() && !message.TargetGrain.IsClient();
}
