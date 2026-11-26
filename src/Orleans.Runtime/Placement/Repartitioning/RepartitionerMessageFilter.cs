
using Orleans.Placement;

namespace Orleans.Runtime.Placement.Repartitioning;

internal interface IRepartitionerMessageFilter
{
    bool IsAcceptable(Message message, out bool isSenderMigratable, out bool isTargetMigratable);
}

internal sealed class RepartitionerMessageFilter(GrainMigratabilityChecker checker) : IRepartitionerMessageFilter
{
    public bool IsAcceptable(Message message, out bool isSenderMigratable, out bool isTargetMigratable)
    {
        isSenderMigratable = false;
        isTargetMigratable = false;

        // 有一些边缘情况可能发生这种情况 i.e。一个 grain 通过 AsReference援引另一种方法,但我们仍然排除了这种可能性。
        // 如果这一 grain 位于该组中,则始终是当地电话(因为目标是本身目标本身),这将增加负转移成本
        // 这样做可能会扭曲这个 grain的搬迁,尽管它不应该,因为每当找到这个 grain ,它仍然会自己发出当地电话。
        if (message.SendingGrain == message.TargetGrain)
        {
            return false;
        }

        isSenderMigratable = checker.IsMigratable(message.SendingGrain.Type, ImmovableKind.Repartitioner);
        isTargetMigratable = checker.IsMigratable(message.TargetGrain.Type, ImmovableKind.Repartitioner);

        // 如果两者都不是偏偏偏偏偏偏差的类型,我们忽视了这一点。但如果其中之一不是,那么我们就允许通过,因为我们想移动 grains 与任何种类的动物一样,如近它们和 grain.
        return isSenderMigratable || isTargetMigratable;
    }
}
