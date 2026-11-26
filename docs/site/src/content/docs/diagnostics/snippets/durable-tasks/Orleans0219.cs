namespace Documentation.Diagnostics.DurableTasks;

public interface IDispatchGrain : IGrainWithStringKey
{
    DurableTask DispatchAsync(string customer);

    DurableTask DispatchAndNotifyAsync(string customer);

    DurableTask DispatchAndInvoiceAsync(string customer);

    DurableTask DispatchAndSurveyAsync(string customer);
}

public sealed class DispatchGrain(IEmailSender email) : DurableGrain, IDispatchGrain
{
#pragma warning disable ORLEANS0219
    // <orleans0219_violation>
    public async DurableTask DispatchAsync(string customer)
    {
        // 启动通知并丢弃其手柄:通知的失败没有代码。
        await Notify(customer).WithId("notify").ScheduleAsync();
        await Pack().WithId("pack");
    }
    // </orleans0219_violation>
#pragma warning restore ORLEANS0219

    // <orleans0219_fix_await>
    public async DurableTask DispatchAndNotifyAsync(string customer)
    {
        // 通知在身体包装时运行,身体在完成之前观察结果。
        var notice = await Notify(customer).WithId("notify").ScheduleAsync();
        await Pack().WithId("pack");
        await notice;
    }
    // </orleans0219_fix_await>

    // <orleans0219_fix_combinator>
    public async DurableTask DispatchAndInvoiceAsync(string customer)
    {
        var notice = await Notify(customer).WithId("notify").ScheduleAsync();
        var invoice = await Invoice(customer).WithId("invoice").ScheduleAsync();
        await Pack().WithId("pack");
        await ScheduledTask.WhenAll([notice, invoice]);
    }
    // </orleans0219_fix_combinator>

    // <orleans0219_fix_detached>
    public async DurableTask DispatchAndSurveyAsync(string customer)
    {
        await Pack().WithId("pack");

        // 以独立根基任务开始的耐用 grain 呼叫,在完成执行后继续;
        // 删除。
        await GrainFactory.GetGrain<ICustomerSurveys>(customer).SendSurveyAsync(customer).WithId("survey").Detached().ScheduleAsync();
    }
    // </orleans0219_fix_detached>

    private static async DurableTask<bool> Pack() => true;

    private async DurableTask Notify(string customer) =>
        await email.SendAsync(customer, "Your order is on its way", DurableTask.Current.CancellationToken);

    private async DurableTask Invoice(string customer) =>
        await email.SendAsync(customer, "Your invoice", DurableTask.Current.CancellationToken);
}
