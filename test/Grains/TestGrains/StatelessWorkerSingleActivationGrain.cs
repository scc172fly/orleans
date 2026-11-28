using Orleans.Concurrency;
using Orleans.Runtime;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains;

/// <summary>
/// 跟踪无状态工作者 Grain 仍存活（尚未停用）的激活实例，使测试能够断言
/// 停用竞争过程中始终不会超过配置的每 Silo 并发激活数量上限。
/// </summary>
public sealed class StatelessWorkerSingleActivationTracker
{
    private int _current;
    private int _maxObserved;

    public int Current => Volatile.Read(ref _current);

    public int MaxObserved => Volatile.Read(ref _maxObserved);

    public void OnActivate()
    {
        var c = Interlocked.Increment(ref _current);
        InterlockedMax(ref _maxObserved, c);
    }

    public void OnDeactivate() => Interlocked.Decrement(ref _current);

    public void Reset()
    {
        Interlocked.Exchange(ref _current, 0);
        Interlocked.Exchange(ref _maxObserved, 0);
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int initial;
        do
        {
            initial = Volatile.Read(ref location);
            if (value <= initial)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref location, value, initial) != initial);
    }
}

[StatelessWorker(maxLocalWorkers: 1)]
public class StatelessWorkerSingleActivationGrain : Grain, IStatelessWorkerSingleActivationGrain
{
    private readonly StatelessWorkerSingleActivationTracker _tracker;

    public StatelessWorkerSingleActivationGrain(StatelessWorkerSingleActivationTracker tracker)
    {
        _tracker = tracker;
        _tracker.OnActivate();
    }

    public Task DoWork() => Task.CompletedTask;

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _tracker.OnDeactivate();
        return Task.CompletedTask;
    }
}
