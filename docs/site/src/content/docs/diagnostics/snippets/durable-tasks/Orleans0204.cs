namespace Documentation.Diagnostics.DurableTasks;

public sealed class TicketGrain : DurableGrain
{
    private readonly IDurableValue<int> _lastNumber;

    public TicketGrain() => _lastNumber = StateManager.GetOrAddValue<int>("last-number");

#pragma warning disable ORLEANS0204
    // <orleans0204_violation>
    public async DurableTask<string> IssueAsync()
    {
        var number = 0;
        var reference = Guid.Empty;
        DurableTask.RunInline("allocate", () =>
        {
            // 重放返回所记录的结果,而不运行效果,所以这些任务不会发生。
            number = ++_lastNumber.Value;
            reference = Guid.NewGuid();
        });

        await Print(number, reference).WithId("print");
        return $"{number}-{reference:N}";
    }
    // </orleans0204_violation>
#pragma warning restore ORLEANS0204

    // <orleans0204_fix>
    public async DurableTask<string> IssueOnceAsync()
    {
        // 效果返回其输出, 所以每个重播都接收记录值。
        var (number, reference) = DurableTask.RunInline("allocate", () => (++_lastNumber.Value, Guid.NewGuid()));

        await Print(number, reference).WithId("print");
        return $"{number}-{reference:N}";
    }
    // </orleans0204_fix>

    // <orleans0204_fix_record>
    [GenerateSerializer, Immutable]
    public sealed record Allocation(int Number, Guid Reference, DateTimeOffset IssuedAt);

    public async DurableTask<Allocation> IssueAllocationAsync()
    {
        var allocation = DurableTask.RunInline(
            "allocate",
            () => new Allocation(++_lastNumber.Value, Guid.NewGuid(), DateTimeOffset.UtcNow));

        await Print(allocation.Number, allocation.Reference).WithId("print");
        return allocation;
    }
    // </orleans0204_fix_record>

    private static async DurableTask<bool> Print(int number, Guid reference) => number > 0 && reference != Guid.Empty;
}
