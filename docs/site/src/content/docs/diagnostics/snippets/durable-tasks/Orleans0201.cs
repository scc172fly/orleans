namespace Documentation.Diagnostics.DurableTasks;

public sealed class PaymentGrain(IPaymentGateway payments) : DurableGrain
{
    private static readonly DurableSignal<Decision> Approved = new("approved");

#pragma warning disable ORLEANS0201
    // <orleans0201_violation>
    public async DurableTask<Charge?> PayAsync(decimal amount)
    {
        // 重放会生成另一个键并计算另一个截止时间。
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
        // Grain、任务标识符和运行世代标识在每次重放中都标识本次运行，因此它们共同标识
        // 此次扣款；同一标识符的后续运行使用另一个键扣款。
        var idempotencyKey = $"{this.GetGrainId()}/{DurableTask.Current.Id}/{DurableTask.Current.Incarnation:N}/charge";

        // 逻辑时间：运行时记录它，因此重放计算出相同的截止时间。
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
