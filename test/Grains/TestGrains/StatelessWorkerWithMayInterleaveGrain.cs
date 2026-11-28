using Orleans.Concurrency;
using Orleans.Serialization.Invocation;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains;

[StatelessWorker(1)] // 使用 '1' 强制交错执行，否则只会创建新的工作实例
[MayInterleave(nameof(MayInterleaveMethod))]
public class StatelessWorkerWithMayInterleaveGrain : Grain, IStatelessWorkerWithMayInterleaveGrain
{
    public static bool MayInterleaveMethod(IInvokable req) => req.GetMethodName() == nameof(GoFast);

    public async Task GoSlow(ICallbackGrainObserver callback)
    {
        await callback.WaitAsync();
    }

    public async Task GoFast(ICallbackGrainObserver callback)
    {
        await callback.WaitAsync();
    }
}
