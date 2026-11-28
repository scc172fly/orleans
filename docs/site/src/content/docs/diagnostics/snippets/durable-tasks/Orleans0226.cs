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

        // 延续操作在线程池上运行：状态变更与 Grain 的其他执行轮次竞争，并且
        // 独立于结果提交。
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

        // 延续操作回到激活实例的调度器，状态变更与结果一同提交。
        var rate = await _rates.GetRateAsync(currency, DurableTask.Current.CancellationToken);
        _lastRate.Value = rate;
        return amount * rate;
    }
    // </orleans0226_fix>

    private static async DurableTask<bool> Approve(decimal amount) => amount > 0;
}
