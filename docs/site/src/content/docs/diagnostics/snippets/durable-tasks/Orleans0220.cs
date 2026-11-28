namespace Documentation.Diagnostics.DurableTasks;

public sealed class LoyaltyGrain : DurableGrain
{
    private readonly IDurableValue<int> _points;

    public LoyaltyGrain() => _points = StateManager.GetOrAddValue<int>("points");

#pragma warning disable ORLEANS0220
    // <orleans0220_violation>
    public async DurableTask<int> AwardAsync(string customer, bool firstOrder)
    {
        if (firstOrder)
        {
            // $child-1，仅出现在首次下单的执行实例中。
            await SendWelcome(customer);
        }

        // 根据条件，积分操作标识为 $child-1 或 $child-2，而账单操作标识为 $child-2 或 $child-3。
        var points = DurableTask.RunInline(() => _points.Value += 100);
        await SendStatement(customer, points);
        return points;
    }
    // </orleans0220_violation>
#pragma warning restore ORLEANS0220

    // <orleans0220_fix>
    public async DurableTask<int> AwardNamedAsync(string customer, bool firstOrder)
    {
        if (firstOrder)
        {
            await SendWelcome(customer).WithId("send-welcome");
        }

        // 无论前面执行了哪些操作，每个名称都标识其调用位置。
        var points = DurableTask.RunInline("add-points", () => _points.Value += 100);
        await SendStatement(customer, points).WithId("send-statement");
        return points;
    }
    // </orleans0220_fix>

    private static async DurableTask<bool> SendWelcome(string customer) => customer.Length > 0;

    private static async DurableTask<bool> SendStatement(string customer, int points) => customer.Length > 0 && points > 0;
}
