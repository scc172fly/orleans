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
        // 提醒已发送后，重放会再次执行此行，导致重复计数。
        _remindersSent.Value++;
        await SendReminderAsync(recipient).WithId("send");
        return _remindersSent.Value;
    }

    public async DurableTask ShipWhenPaidAsync()
    {
        // 重放读取 "ship" 步骤写入的状态，转入另一个分支，并跳过已记录的步骤。
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
        // 此效果只执行一次，其变更与记录一同提交。重放返回已记录的计数。
        var sent = DurableTask.RunInline("count", () => ++_remindersSent.Value);
        await SendReminderAsync(recipient).WithId("send");
        return sent;
    }

    public async DurableTask ShipWhenPaidOnceAsync()
    {
        // 重放返回首次运行读取的状态，因此进入相同分支。
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

        // 此后没有持久化操作，因此此变更与任务结果一同提交，重放不会再次执行它。
        return ++_remindersSent.Value;
    }
    // </orleans0200_fix_tail>

    // <orleans0200_fix_step>
    // 本地步骤：整个方法体都是其末尾代码，因此变更与已记录的结果一同提交。
    private async DurableTask<OrderStatus> MarkShipped() => _status.Value = OrderStatus.Shipped;
    // </orleans0200_fix_step>

    private async DurableTask SendReminderAsync(string recipient) =>
        await _email.SendAsync(recipient, "Your order is waiting", DurableTask.Current.CancellationToken);
}
