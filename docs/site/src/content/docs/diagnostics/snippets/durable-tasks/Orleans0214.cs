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
            // 每一次迭代都回回第一次审查, 所以一个拒绝者立即拒绝每一轮。
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
            // 每轮等待自己的信号名:“经审查/回合1”、“经审查/回合2”等等。
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
