namespace Documentation.Diagnostics.DurableTasks;

public sealed class BookingGrain : DurableGrain
{
    private readonly IDurableValue<OrderStatus> _status;

    public BookingGrain() => _status = StateManager.GetOrAddValue<OrderStatus>("status");

#pragma warning disable ORLEANS0212
    // <orleans0212_violation>
    public async DurableTask<OrderResult> BookAsync(string room)
    {
        try
        {
            await Hold(room).WithId("hold");
            await DurableTask.Delay(TimeSpan.FromMinutes(15)).WithId("hold-period");
        }
        catch (OperationCanceledException)
        {
            // 当主机取消尝试时运行运行,例如当激活停止时: 处理人补偿
            // 订票,没有人取消, 并且它的地位改变承诺 而下一次尝试确认
            // 订票。
            _status.Value = OrderStatus.Cancelled;
            return new OrderResult(OrderStatus.Cancelled, DurableTask.Current.TaskCancellationReason);
        }

        return await Confirm(room).WithId("confirm");
    }
    // </orleans0212_violation>
#pragma warning restore ORLEANS0212

    // <orleans0212_fix_filter>
    public async DurableTask<OrderResult> BookOrReleaseAsync(string room)
    {
        try
        {
            await Hold(room).WithId("hold");
            await DurableTask.Delay(TimeSpan.FromMinutes(15)).WithId("hold-period");
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            // 仅运行任务取消。当主机取消尝试时,例外将结束,而下一个
            // 尝试重放任务。
            await Release(room).WithId("release").Shielded();
            _status.Value = OrderStatus.Cancelled;
            return new OrderResult(OrderStatus.Cancelled, DurableTask.Current.TaskCancellationReason);
        }

        return await Confirm(room).WithId("confirm");
    }
    // </orleans0212_fix_filter>

    // <orleans0212_fix_rethrow>
    public async DurableTask<OrderResult> BookOrCancelAsync(string room)
    {
        try
        {
            await Hold(room).WithId("hold");
            await DurableTask.Delay(TimeSpan.FromMinutes(15)).WithId("hold-period");
        }
        catch (OperationCanceledException)
        {
            // 补偿后重播,任务结束就取消,取消尝试也终止。
            await Release(room).WithId("release").Shielded();
            throw;
        }

        return await Confirm(room).WithId("confirm");
    }
    // </orleans0212_fix_rethrow>

    private static async DurableTask<bool> Hold(string room) => room.Length > 0;

    private static async DurableTask<bool> Release(string room) => room.Length > 0;

    private async DurableTask<OrderResult> Confirm(string room)
    {
        _status.Value = OrderStatus.Paid;
        return new(OrderStatus.Paid, room);
    }
}
