namespace Documentation.Diagnostics.DurableTasks;

public sealed class InvoiceGrain(IExchangeRates rates, IEmailSender email) : DurableGrain
{
#pragma warning disable ORLEANS0202
    // <orleans0202_violation>
    public async DurableTask<decimal> InvoiceAsync(string customer, decimal amount, string currency)
    {
        // 重播再次调用服务: 它可以返回另一个利率, 或者在第一次运行成功时失败 。
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
        // 步骤记录了速率, 所以重播返回它而不拨打服务。
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

    // 本地步骤: 重放返回其记录的完成, 不发送邮件 。
    private async DurableTask NotifyAsync(string customer, decimal total) =>
        await email.SendAsync(customer, $"Your invoice for {total:C}", DurableTask.Current.CancellationToken);
    // </orleans0202_fix_step>

    // <orleans0202_fix_tail>
    public async DurableTask<decimal> InvoiceThenNotifyAsync(string customer, decimal amount)
    {
        var total = await Bill(amount).WithId("bill");

        // 之后没有持久操作, 所以只有完成任务的一个运行才能发送电子邮件。 结束的运行
        // 在那日,应否再造一次。
        await email.SendAsync(customer, $"Your invoice for {total:C}", DurableTask.Current.CancellationToken);
        return total;
    }
    // </orleans0202_fix_tail>

    private static async DurableTask<decimal> Bill(decimal amount) => decimal.Round(amount, 2);
}
