namespace Documentation.Diagnostics.DurableTasks;

public sealed class OnboardingGrain(IEmailSender email) : DurableGrain
{
#pragma warning disable ORLEANS0208
    // <orleans0208_violation>
    public async DurableTask OnboardAsync(string customer)
    {
        await Send(customer, "Welcome").WithId("email");
        await DurableTask.Delay(TimeSpan.FromDays(7)).WithId("first-week");

        // 记录为 "email$2"。部署移除欢迎邮件后，重放将此次调用标识为 "email"，
        // 而该标识保存的是欢迎邮件的记录。
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
        // 为循环反复执行的同一个调用位置命名一次：各次调用分别为 "lesson"、"lesson$2" 等。也可以使用
        // 从每个输入派生的名称来标识对应调用。
        foreach (var subject in subjects)
        {
            await Send(customer, subject).WithId("lesson");
        }
    }
    // </orleans0208_fix_loop>

    private async DurableTask Send(string customer, string subject) =>
        await email.SendAsync(customer, subject, DurableTask.Current.CancellationToken);
}
