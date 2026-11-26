namespace Documentation.Diagnostics.DurableTasks;

public sealed class OnboardingGrain(IEmailSender email) : DurableGrain
{
#pragma warning disable ORLEANS0208
    // <orleans0208_violation>
    public async DurableTask OnboardAsync(string customer)
    {
        await Send(customer, "Welcome").WithId("email");
        await DurableTask.Delay(TimeSpan.FromDays(7)).WithId("first-week");

        // 被录为“email 2”。 在部署去除欢迎电子邮件后, 重播将此电话命名为“ email ” 。
        // 并保留欢迎电子邮件的记录。
        await Send(customer, "Tips for your first week").WithId("email");
    }
    // </orleans0208_violation>
#pragma warning restore ORLEANS0208

    // <orleans0208_fix>
    public async DurableTask OnboardWithNamesAsync(string customer)
    {
        await Send(customer, "Welcome").WithId("welcome-email");
        await DurableTask.Delay(TimeSpan.FromDays(7)).WithId("first-week");
        await Send(customer, "Tips for your first week").WithId("tips-email");
    }
    // </orleans0208_fix>

    // <orleans0208_fix_loop>
    public async DurableTask SendSeriesAsync(string customer, string[] subjects)
    {
        // 一个循环重复被命名过一次的呼叫站点:它的发生是“光”、“光2美元”等等。
        // 从每项投入中得出的数据也确认了这一数据。
        foreach (var subject in subjects)
        {
            await Send(customer, subject).WithId("lesson");
        }
    }
    // </orleans0208_fix_loop>

    private async DurableTask Send(string customer, string subject) =>
        await email.SendAsync(customer, subject, DurableTask.Current.CancellationToken);
}
