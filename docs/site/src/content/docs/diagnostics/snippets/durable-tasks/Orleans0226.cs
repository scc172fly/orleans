namespace Documentation.Diagnostics.DurableTasks;

public sealed class ConversionGrain : DurableGrain
{
    private readonly IExchangeRates _rates;
    private readonly IDurableValue<decimal> _lastRate;

    public ConversionGrain(IExchangeRates rates)
    {
        _rates = rates;
        _lastRate = StateManager.GetOrAddValue<decimal>("last-rate");
    }

#pragma warning disable ORLEANS0226
    // <orleans0226_violation>
    public async DurableTask<decimal> ConvertAsync(string currency, decimal amount)
    {
        await Approve(amount).WithId("approve");

        // 继续运行于线性球库:国家变化与 grain其他旋转的顺序相竞,并承诺
        // 与结果分开。
        var rate = await _rates.GetRateAsync(currency, DurableTask.Current.CancellationToken).ConfigureAwait(false);
        _lastRate.Value = rate;
        return amount * rate;
    }
    // </orleans0226_violation>
#pragma warning restore ORLEANS0226

    // <orleans0226_fix>
    public async DurableTask<decimal> ConvertOnSchedulerAsync(string currency, decimal amount)
    {
        await Approve(amount).WithId("approve");

        // 继续返回激活的调度器, 国家变化也随之而来。
        var rate = await _rates.GetRateAsync(currency, DurableTask.Current.CancellationToken);
        _lastRate.Value = rate;
        return amount * rate;
    }
    // </orleans0226_fix>

    private static async DurableTask<bool> Approve(decimal amount) => amount > 0;
}
