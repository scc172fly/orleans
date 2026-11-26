using Orleans.Concurrency;
using Orleans.Runtime;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains;

/// <summary>
/// 无国籍工人的(未停止)启动(尚未停止)运行轨道 grains 测试可以进行
/// 在整个停用赛事中,每个silo 配置的同时激活限值从未超过。
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
