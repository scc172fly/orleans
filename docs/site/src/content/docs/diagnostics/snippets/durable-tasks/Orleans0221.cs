namespace Documentation.Diagnostics.DurableTasks;

public sealed class ReservationGrain : DurableGrain
{
    private static readonly DurableVersion InlineReservation = new("inline-reservation", minSupported: 0, maxSupported: 1);

    private readonly IPaymentGateway _payments;
    private readonly IDurableValue<OrderStatus> _status;

    public ReservationGrain(IPaymentGateway payments)
    {
        _payments = payments;
        _status = StateManager.GetOrAddValue<OrderStatus>("status");
    }

#pragma warning disable ORLEANS0221
    // <orleans0221_violation>
    public async DurableTask<Charge> ReserveAsync(string orderKey, decimal amount)
    {
        // 同步的状态变化, 该步骤作为儿童处决运行 与日记的写作自己。
        await DurableTask.Run(_ => { _status.Value = OrderStatus.Reserved; }).WithId("reserve");
        return await DurableTask.Run(ct => _payments.ChargeAsync(orderKey, amount, ct)).WithId("charge");
    }
    // </orleans0221_violation>
#pragma warning restore ORLEANS0221

    // <orleans0221_fix>
    public async DurableTask<Charge> ReserveInlineAsync(string orderKey, decimal amount)
    {
        // 效果运行在当前的工作项中, 其记录会随着执行的下一期日记的写而生效 。
        DurableTask.RunInline("reserve", () => { _status.Value = OrderStatus.Reserved; });
        return await DurableTask.Run(ct => _payments.ChargeAsync(orderKey, amount, ct)).WithId("charge");
    }
    // </orleans0221_fix>

    // <orleans0221_fix_versioned>
    public async DurableTask<Charge> ReserveVersionedAsync(string orderKey, decimal amount)
    {
        if (InlineReservation.IsAtLeast(1))
        {
            DurableTask.RunInline("reserve", () => { _status.Value = OrderStatus.Reserved; });
        }
        else
        {
            // 记录了步骤重播的处决
#pragma warning disable ORLEANS0221
            await DurableTask.Run(_ => { _status.Value = OrderStatus.Reserved; }).WithId("reserve");
#pragma warning restore ORLEANS0221
        }

        return await DurableTask.Run(ct => _payments.ChargeAsync(orderKey, amount, ct)).WithId("charge");
    }
    // </orleans0221_fix_versioned>
}
