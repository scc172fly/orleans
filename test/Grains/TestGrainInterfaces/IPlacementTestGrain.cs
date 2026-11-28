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
    // LocalContent Grain 场景的接口，其中引导提供程序会在每个 Silo 上激活该 Grain。

    public interface ILocalContentGrain : IGrainWithGuidKey
    {
        Task Init();                            // 仅用于激活此 Grain 的空操作调用。
        Task<object> GetContent();
    }

    public interface ITestContentGrain : IGrainWithIntegerKey
    {
        Task<string> GetRuntimeInstanceId();    // 仅供测试使用
        Task<object> FetchContentFromLocalGrain();
    }

}
