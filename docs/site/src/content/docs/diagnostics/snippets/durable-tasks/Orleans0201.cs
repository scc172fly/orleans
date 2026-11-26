namespace Documentation.Diagnostics.DurableTasks;

public sealed class PaymentGrain(IPaymentGateway payments) : DurableGrain
{
    private static readonly DurableSignal<Decision> Approved = new("approved");

#pragma warning disable ORLEANS0201
    // <orleans0201_violation>
    public async DurableTask<Charge?> PayAsync(decimal amount)
    {
        // 重放绘制另一个密钥,计算另一个截止时间。
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var deadline = DateTimeOffset.UtcNow.AddDays(3);

        var (received, decision) = await Approved.TryWaitAsync(deadline).WithId("approval");
        if (!received || !decision.Approved)
        {
            return null;
        }

        return await DurableTask.Run(ct => payments.ChargeAsync(idempotencyKey, amount, ct)).WithId("charge");
    }
    // </orleans0201_violation>
#pragma warning restore ORLEANS0201

    // <orleans0201_fix>
    public async DurableTask<Charge?> PayOnceAsync(decimal amount)
    {
        // grain、 任务标识符及其化名 在每个重放中运行, 所以它们一起识别
        // 和另一把钥匙下的同一笔识别资料
        var idempotencyKey = $"{this.GetGrainId()}/{DurableTask.Current.Id}/{DurableTask.Current.Incarnation:N}/charge";

        // 逻辑时间:运行时记录它,重放计算相同的最后期限。
        var deadline = DurableTask.Current.UtcNow.AddDays(3);

        var (received, decision) = await Approved.TryWaitAsync(deadline).WithId("approval");
        if (!received || !decision.Approved)
        {
            return null;
        }

        return await DurableTask.Run(ct => payments.ChargeAsync(idempotencyKey, amount, ct)).WithId("charge");
    }
    // </orleans0201_fix>
}
