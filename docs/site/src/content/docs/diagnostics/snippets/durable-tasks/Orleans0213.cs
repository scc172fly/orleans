namespace Documentation.Diagnostics.DurableTasks;

public sealed class AuditGrain(IEmailSender email) : DurableGrain
{
#pragma warning disable ORLEANS0213
    // <orleans0213_violation>
    public async DurableTask CloseAccountAsync(string customer)
    {
        await Close(customer).WithId("close");

        // 身体在电子邮件发送前返回,而未能发送尸体则会越狱。
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

        // 该机构等待着一个步骤:该机构观察其完成和失败,重播回回录记录。
        async DurableTask NotifyClosed(string recipient) =>
            await email.SendAsync(recipient, "Your account is closed", DurableTask.Current.CancellationToken);
    }
    // </orleans0213_fix_step>

    // <orleans0213_fix_func>
    public async DurableTask CloseAccountWithCallbackAsync(string customer, Func<string, Task> onClosed)
    {
        await Close(customer).WithId("close");

        // 代表返回任务,作为机构等待的一个步骤运行。
        await DurableTask.Run(_ => onClosed(customer)).WithId("on-closed");
    }
    // </orleans0213_fix_func>

    private static async DurableTask<bool> Close(string customer) => customer.Length > 0;
}
