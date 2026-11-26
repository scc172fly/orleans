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
            // 以一美元为单位,只执行第一命令
            await SendWelcome(customer);
        }

        // 以1美元或2美元为单位,以2美元为单位,以3美元为单位。
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

        // 每个名字都标明其呼叫站点,无论之前的作业是哪个。
        var points = DurableTask.RunInline("add-points", () => _points.Value += 100);
        await SendStatement(customer, points).WithId("send-statement");
        return points;
    }
    // </orleans0220_fix>

    private static async DurableTask<bool> SendWelcome(string customer) => customer.Length > 0;

    private static async DurableTask<bool> SendStatement(string customer, int points) => customer.Length > 0 && points > 0;
}
