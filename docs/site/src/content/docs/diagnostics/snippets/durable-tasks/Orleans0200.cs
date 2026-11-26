namespace Documentation.Diagnostics.DurableTasks;

public sealed class ReminderGrain : DurableGrain
{
    private readonly IEmailSender _email;
    private readonly IDurableValue<int> _remindersSent;
    private readonly IDurableValue<OrderStatus> _status;

    public ReminderGrain(IEmailSender email)
    {
        _email = email;
        _remindersSent = StateManager.GetOrAddValue<int>("reminders-sent");
        _status = StateManager.GetOrAddValue<OrderStatus>("status");
    }

#pragma warning disable ORLEANS0200
    // <orleans0200_violation>
    public async DurableTask<int> RemindAsync(string recipient)
    {
        // 提醒发送后, 重放再次运行此行, 并两次计数 。
        _remindersSent.Value++;
        await SendReminderAsync(recipient).WithId("send");
        return _remindersSent.Value;
    }

    public async DurableTask ShipWhenPaidAsync()
    {
        // 重播读取“ship”步骤写下的位置, 选择另一分支, 跳过记录的步骤 。
        if (_status.Value == OrderStatus.Paid)
        {
            await MarkShipped().WithId("ship");
        }
    }
    // </orleans0200_violation>
#pragma warning restore ORLEANS0200

    // <orleans0200_fix_once>
    public async DurableTask<int> RemindOnceAsync(string recipient)
    {
        // 效果运行一次, 更改后会随记录一起发生。 重放返回所记录的计数 。
        var sent = DurableTask.RunInline("count", () => ++_remindersSent.Value);
        await SendReminderAsync(recipient).WithId("send");
        return sent;
    }

    public async DurableTask ShipWhenPaidOnceAsync()
    {
        // 重播返回第一个运行读取状态,所以它需要同一个分支。
        var status = DurableTask.RunInline("read-status", () => _status.Value);
        if (status == OrderStatus.Paid)
        {
            await MarkShipped().WithId("ship");
        }
    }
    // </orleans0200_fix_once>

    // <orleans0200_fix_tail>
    public async DurableTask<int> RemindThenCountAsync(string recipient)
    {
        await SendReminderAsync(recipient).WithId("send");

        // 之后没有持久行动,所以这一变化与任务的结果相伴而生,不再重复。
        return ++_remindersSent.Value;
    }
    // </orleans0200_fix_tail>

    // <orleans0200_fix_step>
    // 地方步骤:整个机构是其最终守则,因此它的变化承诺以记录的结果。
    private async DurableTask<OrderStatus> MarkShipped() => _status.Value = OrderStatus.Shipped;
    // </orleans0200_fix_step>

    private async DurableTask SendReminderAsync(string recipient) =>
        await _email.SendAsync(recipient, "Your order is waiting", DurableTask.Current.CancellationToken);
}
