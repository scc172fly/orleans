namespace Documentation.Diagnostics.DurableTasks;

public sealed class FulfillmentGrain(IEmailSender email) : DurableGrain
{
#pragma warning disable ORLEANS0205, CS4014
    // <orleans0205_violation>
    public async DurableTask FulfillAsync(string customer)
    {
        await Pack().WithId("pack");

        // 创建定义后将其丢弃：收据永远不会发送。
        SendReceipt(customer);
    }
    // </orleans0205_violation>
#pragma warning restore ORLEANS0205, CS4014

    // <orleans0205_fix_await>
    public async DurableTask FulfillAndSendAsync(string customer)
    {
        await Pack().WithId("pack");
        await SendReceipt(customer).WithId("receipt");
    }
    // </orleans0205_fix_await>

    // <orleans0205_fix_schedule>
    public async DurableTask FulfillWhileSendingAsync(string customer)
    {
        // 启动收据发送后继续执行；执行实例拥有该任务，并在返回前观察其结果。
        var receipt = await SendReceipt(customer).WithId("receipt").ScheduleAsync();
        await Pack().WithId("pack");
        await receipt;
    }
    // </orleans0205_fix_schedule>

    // <orleans0205_fix_post>
    public async DurableTask FulfillAndPostAsync(string customer)
    {
        await Pack().WithId("pack");

        // 将收据发送暂存到执行实例下一次日志写入中，而不等待它启动。
        var receipt = SendReceipt(customer).WithId("receipt").Post();
        await receipt;
    }
    // </orleans0205_fix_post>

    private static async DurableTask<bool> Pack() => true;

    private async DurableTask SendReceipt(string customer) =>
        await email.SendAsync(customer, "Your receipt", DurableTask.Current.CancellationToken);
}
