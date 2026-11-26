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
        // 其它日记可以写入此更改, 而电子邮件正在发送时, 没有步骤结果 。
        // 然后,该步骤又运行了一次,并两次计数通知。
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

        // 步骤的最后同步代码: 更改在同一个日记中承诺作为步骤结果 。
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
