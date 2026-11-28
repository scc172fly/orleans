namespace Documentation.Diagnostics.DurableTasks;

public sealed class ReminderScheduleGrain(IEmailSender email) : DurableGrain
{
#pragma warning disable ORLEANS0206
    // <orleans0206_violation>
    public async DurableTask RemindAsync(string customer, int reminders)
    {
        var remind = SendReminder(customer);
        for (var i = 0; i < reminders; i++)
        {
            await DurableTask.Delay(TimeSpan.FromDays(1)).WithId($"wait-{i}");

            // 定义已在第一次迭代中运行，因此第二次迭代抛出异常。
            await remind.WithId($"remind-{i}");
        }
    }
    // </orleans0206_violation>
#pragma warning restore ORLEANS0206

    // <orleans0206_fix>
    public async DurableTask RemindEachDayAsync(string customer, int reminders)
    {
        for (var i = 0; i < reminders; i++)
        {
            await DurableTask.Delay(TimeSpan.FromDays(1)).WithId($"wait-{i}");

            // 每次调用都为本次迭代的运行创建新定义。
            await SendReminder(customer).WithId($"remind-{i}");
        }
    }
    // </orleans0206_fix>

    // <orleans0206_fix_factory>
    public async DurableTask RemindTwiceAsync(string customer)
    {
        // 工厂为每次运行创建一个定义。
        DurableTask Remind() => SendReminder(customer);

        await Remind().WithId("first-reminder");
        await DurableTask.Delay(TimeSpan.FromDays(3)).WithId("grace");
        await Remind().WithId("final-reminder");
    }
    // </orleans0206_fix_factory>

    private async DurableTask SendReminder(string customer) =>
        await email.SendAsync(customer, "Your cart is waiting", DurableTask.Current.CancellationToken);
}
