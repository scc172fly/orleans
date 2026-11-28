namespace Documentation.Diagnostics.DurableTasks;

public sealed class NewsletterGrain(IEmailSender email) : DurableGrain
{
#pragma warning disable ORLEANS0222
    // <orleans0222_violation>
    public async DurableTask SubscribeAsync()
    {
        // SendWelcome 在第一个 await 处返回，因此步骤在邮件发送前就记录了完成状态。
        await DurableTask.Run(SendWelcome).WithId("welcome");
    }

    private async void SendWelcome(CancellationToken cancellationToken) =>
        await email.SendAsync(this.GetPrimaryKeyString(), "Welcome", cancellationToken);
    // </orleans0222_violation>
#pragma warning restore ORLEANS0222

    // <orleans0222_fix_task>
    public async DurableTask SubscribeAndWelcomeAsync()
    {
        // 返回的任务完成时（即邮件发送后），该步骤才完成。
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
