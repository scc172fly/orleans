
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

        // 某些边界情况下可能发生这种情况，例如 Grain 通过 AsReference<> 调用自身的另一方法，但仍将其排除，
        // 因为无论该 Grain 位于集群何处，这始终是本地调用（目标为自身）。若计入此调用，就会增加负迁移成本，
        // 导致对该 Grain 潜在迁移的评估产生偏差；而实际上无论该 Grain 位于何处，对自身的调用始终是本地调用。
        if (message.SendingGrain == message.TargetGrain)
        {
            return false;
        }

        isSenderMigratable = checker.IsMigratable(message.SendingGrain.Type, ImmovableKind.Repartitioner);
        isTargetMigratable = checker.IsMigratable(message.TargetGrain.Type, ImmovableKind.Repartitioner);

        // 如果两者均为不可迁移类型，则忽略此边；如果只有一个不可迁移，则允许通过，因为与其他 Grain 类型一样，我们希望将 Grain 移到更靠近它的位置。
        return isSenderMigratable || isTargetMigratable;
    }
}
