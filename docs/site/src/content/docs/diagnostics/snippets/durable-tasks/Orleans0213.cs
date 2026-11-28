namespace Documentation.Diagnostics.DurableTasks;

public sealed class AuditGrain(IEmailSender email) : DurableGrain
{
#pragma warning disable ORLEANS0213
    // <orleans0213_violation>
    public async DurableTask CloseAccountAsync(string customer)
    {
        await Close(customer).WithId("close");

        // 任务体在电子邮件发送前返回，发送失败的异常不会被任务体捕获。
        NotifyClosed(customer);

        async void NotifyClosed(string recipient) =>
            await email.SendAsync(recipient, "Your account is closed", DurableTask.Current.CancellationToken);
    }
    // </orleans0213_violation>
#pragma warning restore ORLEANS0213

    // <orleans0213_fix_step>
    public async DurableTask CloseAccountAndNotifyAsync(string customer)
    {
        await Close(customer).WithId("close");
        await NotifyClosed(customer).WithId("notify");

        // 由任务体等待的步骤：任务体观察其完成及失败，重放返回其记录。
        async DurableTask NotifyClosed(string recipient) =>
            await email.SendAsync(recipient, "Your account is closed", DurableTask.Current.CancellationToken);
    }
    // </orleans0213_fix_step>

    // <orleans0213_fix_func>
    public async DurableTask CloseAccountWithCallbackAsync(string customer, Func<string, Task> onClosed)
    {
        await Close(customer).WithId("close");

        // 返回 Task 的委托，作为由任务体等待的步骤运行。
        await DurableTask.Run(_ => onClosed(customer)).WithId("on-closed");
    }
    // </orleans0213_fix_func>

    private static async DurableTask<bool> Close(string customer) => customer.Length > 0;
}
