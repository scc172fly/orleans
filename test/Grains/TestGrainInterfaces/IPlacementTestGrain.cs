using System.Net;
using Orleans.Runtime;

namespace UnitTests.GrainInterfaces
{
    public interface IPlacementTestGrain : IGrainWithGuidKey
    {
        Task<IPEndPoint> GetEndpoint();
        Task<string> GetRuntimeInstanceId();
        Task<string> GetActivationId();
        Task StartLocalGrains(List<Guid> keys);
        Task<Guid> StartPreferLocalGrain(Guid key);
        Task<List<IPEndPoint>> SampleLocalGrainEndpoint(Guid key, int sampleSize);
        Task Nop();
        Task EnableOverloadDetection(bool enabled);
        Task LatchOverloaded();
        Task UnlatchOverloaded();
        Task LatchCpuUsage(float value);
        Task UnlatchCpuUsage();
        Task<SiloAddress> GetLocation();
    }

    public interface IActivationCountBasedPlacementTestGrain : IPlacementTestGrain
    { }

    public interface IRandomPlacementTestGrain : IPlacementTestGrain
    { }

    public interface IPreferLocalPlacementTestGrain : IPlacementTestGrain
    { }

    public interface IStatelessWorkerPlacementTestGrain : IPlacementTestGrain
    {
        ValueTask<int> GetWorkerLimit();
    }

    public interface IOtherStatelessWorkerPlacementTestGrain : IStatelessWorkerPlacementTestGrain
    {
    }

    internal interface IDefaultPlacementTestGrain
    {
        bool IsDefaultPlacementRandom();
    }

    //----------------------------------------------------------//
    // 用于 LocalContent grain 情况,当 grain 启动于每个 silo 由靴子陷阱提供者。

    public interface ILocalContentGrain : IGrainWithGuidKey
    {
        Task Init();                            // 假电话,只需激活此 grain。
        Task<object> GetContent();
    }

    public interface ITestContentGrain : IGrainWithIntegerKey
    {
        Task<string> GetRuntimeInstanceId();    // 仅用于测试
        Task<object> FetchContentFromLocalGrain();
    }

}
