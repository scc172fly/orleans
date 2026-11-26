namespace Documentation.Diagnostics.DurableTasks;

public sealed class TrialGrain(IEmailSender email) : DurableGrain
{
#pragma warning disable ORLEANS0211
    // <orleans0211_violation>
    public async DurableTask ExpireTrialAsync(string customer)
    {
        // 等待生命在记忆中:失能会失去它,重播又再等待整整14天。
        await Task.Delay(TimeSpan.FromDays(14));
        await Notify(customer, "Your trial has ended").WithId("expired");
    }
    // </orleans0211_violation>
#pragma warning restore ORLEANS0211

    // <orleans0211_fix_delay>
    public async DurableTask ExpireTrialDurablyAsync(string customer)
    {
        // 运行时间记录了到期时间,而持久工作在到期时重新启动 grain 。
        await DurableTask.Delay(TimeSpan.FromDays(14)).WithId("trial");
        await Notify(customer, "Your trial has ended").WithId("expired");
    }
    // </orleans0211_fix_delay>

    // <orleans0211_fix_delay_until>
    public async DurableTask RemindBeforeExpiryAsync(string customer, DateTimeOffset expiresAt)
    {
        await DurableTask.DelayUntil(expiresAt.AddDays(-1)).WithId("day-before");
        await Notify(customer, "Your trial ends tomorrow").WithId("reminder");
    }
    // </orleans0211_fix_delay_until>

    // <orleans0211_fix_timeout>
    public async DurableTask<bool> NotifyWithTimeoutAsync(string customer)
    {
        try
        {
            // 而不是与 Task.Delay:一个从第一次运行起计数的耐久超时赛跑。
            await Notify(customer, "Your trial has ended").WaitAsync(TimeSpan.FromMinutes(5)).WithId("notify");
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
    // </orleans0211_fix_timeout>

    private async DurableTask Notify(string customer, string subject) =>
        await email.SendAsync(customer, subject, DurableTask.Current.CancellationToken);
}
