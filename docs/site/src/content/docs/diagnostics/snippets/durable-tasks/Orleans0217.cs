namespace Documentation.Diagnostics.DurableTasks;

public sealed class RoomHoldGrain(IRoomInventory rooms) : DurableGrain
{
#pragma warning disable ORLEANS0217
    // <orleans0217_violation>
    public async DurableTask<OrderResult> HoldAsync(string room)
    {
        try
        {
            await Hold(room).WithId("hold");
            await DurableTask.Delay(TimeSpan.FromMinutes(15)).WithId("hold-period");
            return await Confirm(room).WithId("confirm");
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            // 任务取消已使 CancellationToken 进入取消状态，因此释放操作立即结束。
            await rooms.ReleaseAsync(room, DurableTask.Current.CancellationToken);
            return new OrderResult(OrderStatus.Cancelled, DurableTask.Current.TaskCancellationReason);
        }
    }
    // </orleans0217_violation>
#pragma warning restore ORLEANS0217

    // <orleans0217_fix_attempt_token>
    public async DurableTask<OrderResult> HoldOrReleaseAsync(string room)
    {
        try
        {
            await Hold(room).WithId("hold");
            await DurableTask.Delay(TimeSpan.FromMinutes(15)).WithId("hold-period");
            return await Confirm(room).WithId("confirm");
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            // AttemptCancellationToken 仅在宿主取消执行尝试时取消，因此释放操作可运行至完成。
            await rooms.ReleaseAsync(room, DurableTask.Current.AttemptCancellationToken);
            return new OrderResult(OrderStatus.Cancelled, DurableTask.Current.TaskCancellationReason);
        }
    }
    // </orleans0217_fix_attempt_token>

    // <orleans0217_fix_shielded_step>
    public async DurableTask<OrderResult> HoldOrReleaseOnceAsync(string room)
    {
        try
        {
            await Hold(room).WithId("hold");
            await DurableTask.Delay(TimeSpan.FromMinutes(15)).WithId("hold-period");
            return await Confirm(room).WithId("confirm");
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            // 任务取消不会传播到受取消屏蔽保护的步骤，该步骤记录释放操作。
            await DurableTask.Run(ct => rooms.ReleaseAsync(room, ct)).WithId("release").Shielded();
            return new OrderResult(OrderStatus.Cancelled, DurableTask.Current.TaskCancellationReason);
        }
    }
    // </orleans0217_fix_shielded_step>

    private static async DurableTask<bool> Hold(string room) => room.Length > 0;

    private static async DurableTask<OrderResult> Confirm(string room) => new(OrderStatus.Reserved, room);
}
