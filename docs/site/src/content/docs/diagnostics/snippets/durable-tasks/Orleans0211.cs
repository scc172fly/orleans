namespace Documentation.Diagnostics.DurableTasks;

public sealed class TrialGrain(IEmailSender email) : DurableGrain
{
#pragma warning disable ORLEANS0211
    // <orleans0211_violation>
    public async DurableTask ExpireTrialAsync(string customer)
    {
        // 等待仅存在于内存：停用会使其丢失，重放将再次等待完整的 14 天。
        await Task.Delay(TimeSpan.FromDays(14));
        await Notify(customer, "Your trial has ended").WithId("expired");
    }
    // </orleans0211_violation>
#pragma warning restore ORLEANS0211

    // <orleans0211_fix_delay>
    public async DurableTask ExpireTrialDurablyAsync(string customer)
    {
        // 运行时记录到期时间，Durable Jobs 在到期时重新激活 Grain。
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
            // 代替让工作与 Task.Delay 竞争：持久化超时从首次运行开始计时。
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
