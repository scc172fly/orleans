namespace Documentation.Diagnostics.DurableTasks;

public sealed class InvoiceGrain(IExchangeRates rates, IEmailSender email) : DurableGrain
{
#pragma warning disable ORLEANS0202
    // <orleans0202_violation>
    public async DurableTask<decimal> InvoiceAsync(string customer, decimal amount, string currency)
    {
        // 重放会再次调用服务：可能返回不同汇率，或在首次运行成功之处失败。
        var rate = await rates.GetRateAsync(currency, DurableTask.Current.CancellationToken);
        var total = await Bill(amount * rate).WithId("bill");
        await DurableTask.Delay(TimeSpan.FromDays(30)).WithId("due");
        return total;
    }
    // </orleans0202_violation>
#pragma warning restore ORLEANS0202

    // <orleans0202_fix_run>
    public async DurableTask<decimal> InvoiceWithRecordedRateAsync(string customer, decimal amount, string currency)
    {
        // 步骤记录汇率，因此重放直接返回它，而不会调用服务。
        var rate = await DurableTask.Run(ct => rates.GetRateAsync(currency, ct)).WithId("rate");
        var total = await Bill(amount * rate).WithId("bill");
        await DurableTask.Delay(TimeSpan.FromDays(30)).WithId("due");
        return total;
    }
    // </orleans0202_fix_run>

    // <orleans0202_fix_step>
    public async DurableTask<decimal> InvoiceAndNotifyAsync(string customer, decimal amount)
    {
        var total = await Bill(amount).WithId("bill");
        await NotifyAsync(customer, total).WithId("notify");
        return total;
    }

    // 本地步骤：重放返回已记录的完成结果，而不会再次发送电子邮件。
    private async DurableTask NotifyAsync(string customer, decimal total) =>
        await email.SendAsync(customer, $"Your invoice for {total:C}", DurableTask.Current.CancellationToken);
    // </orleans0202_fix_step>

    // <orleans0202_fix_tail>
    public async DurableTask<decimal> InvoiceThenNotifyAsync(string customer, decimal amount)
    {
        var total = await Bill(amount).WithId("bill");

        // 此后没有持久化操作，因此只有完成任务的运行才发送邮件。如果运行在
        // 结果提交前结束，邮件会再次发送。
        await email.SendAsync(customer, $"Your invoice for {total:C}", DurableTask.Current.CancellationToken);
        return total;
    }
    // </orleans0202_fix_tail>

    private static async DurableTask<decimal> Bill(decimal amount) => decimal.Round(amount, 2);
}
