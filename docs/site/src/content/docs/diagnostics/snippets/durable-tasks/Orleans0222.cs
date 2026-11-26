namespace Documentation.Diagnostics.DurableTasks;

public sealed class NewsletterGrain(IEmailSender email) : DurableGrain
{
#pragma warning disable ORLEANS0222
    // <orleans0222_violation>
    public async DurableTask SubscribeAsync()
    {
        // SendWelcome 返回时, 第一次等待后, 所以步骤记录在电子邮件发送前完成 。
        await DurableTask.Run(SendWelcome).WithId("welcome");
    }

    private async void SendWelcome(CancellationToken cancellationToken) =>
        await email.SendAsync(this.GetPrimaryKeyString(), "Welcome", cancellationToken);
    // </orleans0222_violation>
#pragma warning restore ORLEANS0222

    // <orleans0222_fix_task>
    public async DurableTask SubscribeAndWelcomeAsync()
    {
        // 返回的任务完成后, 电子邮件发送后, 步骤将完成 。
        await DurableTask.Run(SendWelcomeAsync).WithId("welcome");
    }

    private Task SendWelcomeAsync(CancellationToken cancellationToken) =>
        email.SendAsync(this.GetPrimaryKeyString(), "Welcome", cancellationToken);
    // </orleans0222_fix_task>

    // <orleans0222_fix_durable_method>
    public async DurableTask SubscribeWithStepAsync() => await Welcome().WithId("welcome");

    private async DurableTask Welcome() =>
        await email.SendAsync(this.GetPrimaryKeyString(), "Welcome", DurableTask.Current.CancellationToken);
    // </orleans0222_fix_durable_method>
}
