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
        // 启动通知后丢弃其句柄：没有任何代码能观察到通知发送失败。
        await Notify(customer).WithId("notify").ScheduleAsync();
        await Pack().WithId("pack");
    }
    // </orleans0219_violation>
#pragma warning restore ORLEANS0219

    // <orleans0219_fix_await>
    public async DurableTask DispatchAndNotifyAsync(string customer)
    {
        // 通知发送与任务体的打包工作同时进行，任务体在完成前观察其结果。
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

        // 持久化 Grain 调用，作为独立根任务启动；即使当前执行实例完成并且
        // 被删除，该根任务仍会继续运行。
        await GrainFactory.GetGrain<ICustomerSurveys>(customer).SendSurveyAsync(customer).WithId("survey").Detached().ScheduleAsync();
    }
    // </orleans0219_fix_detached>

    private static async DurableTask<bool> Pack() => true;

    private async DurableTask Notify(string customer) =>
        await email.SendAsync(customer, "Your order is on its way", DurableTask.Current.CancellationToken);

    private async DurableTask Invoice(string customer) =>
        await email.SendAsync(customer, "Your invoice", DurableTask.Current.CancellationToken);
}
