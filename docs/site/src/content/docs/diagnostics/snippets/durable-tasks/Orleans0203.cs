namespace Documentation.Diagnostics.DurableTasks;

public sealed class ShipmentGrain : DurableGrain
{
    private readonly IEmailSender _email;
    private readonly IDurableValue<OrderStatus> _status;
    private readonly IDurableValue<int> _notices;

    public ShipmentGrain(IEmailSender email)
    {
        _email = email;
        _status = StateManager.GetOrAddValue<OrderStatus>("status");
        _notices = StateManager.GetOrAddValue<int>("notices");
    }

#pragma warning disable ORLEANS0203
    // <orleans0203_violation>
    private async DurableTask<int> NotifyShippedAsync(string customer)
    {
        // 在发送电子邮件期间，另一次日志写入可能只提交此变更，而没有提交步骤结果。
        // 此步骤随后再次执行，导致该通知重复计数。
        _notices.Value++;
        await _email.SendAsync(customer, "Your order has shipped", DurableTask.Current.CancellationToken);
        return _notices.Value;
    }
    // </orleans0203_violation>
#pragma warning restore ORLEANS0203

    // <orleans0203_fix_reorder>
    private async DurableTask<int> NotifyShippedThenCountAsync(string customer)
    {
        await _email.SendAsync(customer, "Your order has shipped", DurableTask.Current.CancellationToken);

        // 步骤末尾的同步代码：变更与步骤结果在同一次日志写入中提交。
        return ++_notices.Value;
    }
    // </orleans0203_fix_reorder>

    // <orleans0203_fix_split>
    public async DurableTask<int> ShipAsync(string customer)
    {
        DurableTask.RunInline("shipped", () => _status.Value = OrderStatus.Shipped);
        await DurableTask.Run(ct => _email.SendAsync(customer, "Your order has shipped", ct)).WithId("notify");
        return DurableTask.RunInline("count", () => ++_notices.Value);
    }
    // </orleans0203_fix_split>
}
