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
            // 宿主取消执行尝试时也会运行此处理程序，例如激活实例停止时：处理程序会补偿
            // 实际无人取消的预订，并且其状态变更会在下一次执行尝试确认
            // 该预订期间提交。
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
            // 仅在任务取消时运行。宿主取消执行尝试时，异常使本次尝试结束，下一次
            // 尝试重放该任务。
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
            // 补偿完成后重新抛出异常，因此任务以取消状态结束，被取消的执行尝试也会结束。
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
