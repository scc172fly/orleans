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
            // 任务取消已取消 CancellationToken, 所以解密会立刻结束 。
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
            // AttemptCancellationToken 只有在主机取消尝试时才会取消, 所以解密将持续到完成 。
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
            // 取消任务没有达到屏蔽的一步, 而该步骤记录了释放。
            await DurableTask.Run(ct => rooms.ReleaseAsync(room, ct)).WithId("release").Shielded();
            return new OrderResult(OrderStatus.Cancelled, DurableTask.Current.TaskCancellationReason);
        }
    }
    // </orleans0217_fix_shielded_step>

    private static async DurableTask<bool> Hold(string room) => room.Length > 0;

    private static async DurableTask<OrderResult> Confirm(string room) => new(OrderStatus.Reserved, room);
}
