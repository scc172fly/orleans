namespace Documentation.Diagnostics.DurableTasks;

public sealed class ContractGrain : DurableGrain
{
    public static readonly DurableSignal<Decision> Reviewed = new("reviewed");

#pragma warning disable ORLEANS0214
    // <orleans0214_violation>
    public async DurableTask<bool> NegotiateAsync(int rounds)
    {
        for (var round = 1; round <= rounds; round++)
        {
            // 每次迭代都返回第一次评审结果，因此一次拒绝会同时拒绝所有轮次。
            var review = await Reviewed.WaitAsync();
            if (review.Approved)
            {
                return true;
            }
        }

        return false;
    }
    // </orleans0214_violation>
#pragma warning restore ORLEANS0214

    // <orleans0214_fix>
    public async DurableTask<bool> NegotiateEachRoundAsync(int rounds)
    {
        for (var round = 1; round <= rounds; round++)
        {
            // 每一轮等待各自的信号名称："reviewed/round-1"、"reviewed/round-2" 等。
            var review = await Reviewed.For($"round-{round}").WaitAsync();
            if (review.Approved)
            {
                return true;
            }
        }

        return false;
    }
    // </orleans0214_fix>
}
