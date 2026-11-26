using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using Tester;
using UnitTests.GrainInterfaces;
using Xunit;

namespace TestExtensions.Runners;

[TestSuite("Functional")]
[TestArea("Persistence")]
public abstract class GrainPersistenceTestsRunner : OrleansTestingBase
{
    private readonly ITestOutputHelper output;
    private readonly string grainNamespace;
    private readonly BaseTestClusterFixture fixture;
    protected readonly ILogger logger;
    protected TestCluster HostedCluster { get; private set; }
    private static readonly double timingFactor = TestUtils.CalibrateTimings();
    private const int LoopIterations_Grain = 1000;
    private const int BatchSize = 100;
    private const int MaxReadTime = 200;
    private const int MaxWriteTime = 2000;

    protected GrainPersistenceTestsRunner(ITestOutputHelper output, BaseTestClusterFixture fixture, string grainNamespace = "UnitTests.Grains")
    {
        this.output = output;
        this.fixture = fixture;
        this.grainNamespace = grainNamespace;
        logger = fixture.Logger;
        HostedCluster = fixture.HostedCluster;
    }

    protected bool DeleteStateOnClear { get; init; }
    protected bool IsDurableStorage { get; init; } = true;
    protected bool DistinguishesGenericGrainTypeParameters { get; init; } = true;

    protected TimeSpan PerformanceTestTimeout { get; init; } = TimeSpan.FromSeconds(90);

    public IGrainFactory GrainFactory => fixture.GrainFactory;

    [Fact, TestCategory("Functional")]
    public async Task ClearStateAsync_Before_WriteStateAsync()
    {
        var grain = GrainFactory.GetGrain<IGrainStorageTestGrain>(Guid.NewGuid(), grainNamespace);

        await grain.DoDelete();

        var state = await grain.GetStateAsync();
        Assert.False(state.RecordExists);
        Assert.NotNull(state.State);
    }

    [Fact, TestCategory("Functional")]
    public async Task Grain_GrainStorage_Delete()
    {
        var grain = GrainFactory.GetGrain<IGrainStorageTestGrain>(Guid.NewGuid(), grainNamespace);

        await grain.DoWrite(1);
        var state = await grain.GetStateAsync();
        Assert.True(state.RecordExists);
        Assert.NotNull(state.ETag);
        Assert.NotNull(state.State);

        await grain.DoDelete();

        state = await grain.GetStateAsync();
        Assert.False(state.RecordExists);
        if (DeleteStateOnClear)
        {
            Assert.Null(state.ETag);
        }

        Assert.NotNull(state.State);

        var val = await grain.GetValue(); // 不如这样扔?
        Assert.Equal(0, val);  // "在删除后值"

        await grain.DoWrite(2);

        val = await grain.GetValue();
        Assert.Equal(2, val);  // “删除+新写字后值”
    }

    [Fact, TestCategory("Functional")]
    public async Task Grain_GrainStorage_Read()
    {
        var id = Guid.NewGuid();
        var grain = GrainFactory.GetGrain<IGrainStorageTestGrain>(id, grainNamespace);

        var val = await grain.GetValue();

        Assert.Equal(0, val);  // "原价值"

        var state = await grain.GetStateAsync();
        Assert.False(state.RecordExists);
        Assert.NotNull(state.State);
    }

    [Fact, TestCategory("Functional")]
    public async Task Grain_GuidKey_GrainStorage_Read_Write()
    {
        var id = Guid.NewGuid();
        var grain = GrainFactory.GetGrain<IGrainStorageTestGrain>(id, grainNamespace);

        var val = await grain.GetValue();

        Assert.Equal(0, val);  // "原价值"

        await grain.DoWrite(1);
        val = await grain.GetValue();
        Assert.Equal(1, val);  // “写后值”

        await grain.DoWrite(2);
        val = await grain.GetValue();
        Assert.Equal(2, val);  // "写二后的价值"

        val = await grain.DoRead();

        Assert.Equal(2, val);  // 读完后的价值
    }

    [Fact, TestCategory("Functional")]
    public async Task Grain_LongKey_GrainStorage_Read_Write()
    {
        long id = Random.Shared.Next();
        var grain = GrainFactory.GetGrain<IGrainStorageTestGrain_LongKey>(id, grainNamespace);

        var val = await grain.GetValue();

        Assert.Equal(0, val);  // "原价值"

        await grain.DoWrite(1);
        val = await grain.GetValue();
        Assert.Equal(1, val);  // “写后值”

        await grain.DoWrite(2);
        val = await grain.GetValue();
        Assert.Equal(2, val);  // "写二后的价值"

        val = await grain.DoRead();

        Assert.Equal(2, val);  // 读完后的价值
    }

    [Fact, TestCategory("Functional")]
    public async Task Grain_LongKeyExtended_GrainStorage_Read_Write()
    {
        long id = Random.Shared.Next();
        var extKey = Random.Shared.Next().ToString(CultureInfo.InvariantCulture);

        var
            grain = GrainFactory.GetGrain<IGrainStorageTestGrain_LongExtendedKey>(id, extKey, grainNamespace);

        var val = await grain.GetValue();

        Assert.Equal(0, val);  // "原价值"

        await grain.DoWrite(1);
        val = await grain.GetValue();
        Assert.Equal(1, val);  // “写后值”

        await grain.DoWrite(2);
        val = await grain.GetValue();
        Assert.Equal(2, val);  // "写二后的价值"

        val = await grain.DoRead();
        Assert.Equal(2, val);  // “ DoRead之后值”

        val = await grain.GetValue();
        Assert.Equal(2, val);  // 读完后的价值

        var extKeyValue = await grain.GetExtendedKeyValue();
        Assert.Equal(extKey, extKeyValue);  // "扩展密钥"
    }

    [Fact, TestCategory("Functional")]
    public async Task Grain_GuidKeyExtended_GrainStorage_Read_Write()
    {
        var id = Guid.NewGuid();
        var extKey = Random.Shared.Next().ToString(CultureInfo.InvariantCulture);

        var
            grain = GrainFactory.GetGrain<IGrainStorageTestGrain_GuidExtendedKey>(id, extKey, grainNamespace);

        var val = await grain.GetValue();

        Assert.Equal(0, val);  // "原价值"

        await grain.DoWrite(1);
        val = await grain.GetValue();
        Assert.Equal(1, val);  // “写后值”

        await grain.DoWrite(2);
        val = await grain.GetValue();
        Assert.Equal(2, val);  // "写二后的价值"

        val = await grain.DoRead();
        Assert.Equal(2, val);  // “ DoRead之后值”

        val = await grain.GetValue();
        Assert.Equal(2, val);  // 读完后的价值

        var extKeyValue = await grain.GetExtendedKeyValue();
        Assert.Equal(extKey, extKeyValue);  // "扩展密钥"
    }

    [Fact, TestCategory("Functional")]
    public async Task Grain_Generic_GrainStorage_Read_Write()
    {
        long id = Random.Shared.Next();

        var grain = GrainFactory.GetGrain<IGrainStorageGenericGrain<int>>(id, grainNamespace);

        var val = await grain.GetValue();

        Assert.Equal(0, val);  // "原价值"

        await grain.DoWrite(1);
        val = await grain.GetValue();
        Assert.Equal(1, val);  // “写后值”

        await grain.DoWrite(2);
        val = await grain.GetValue();
        Assert.Equal(2, val);  // "写二后的价值"

        val = await grain.DoRead();

        Assert.Equal(2, val);  // 读完后的价值
    }

    [Fact, TestCategory("Functional")]
    public async Task Grain_NestedGeneric_GrainStorage_Read_Write()
    {
        long id = Random.Shared.Next();

        var grain = GrainFactory.GetGrain<IGrainStorageGenericGrain<List<int>>>(id, grainNamespace);

        var val = await grain.GetValue();

        Assert.Null(val);  // "原价值"

        await grain.DoWrite(new List<int> { 1 });
        val = await grain.GetValue();
        Assert.Equal(new List<int> { 1 }, val);  // “写后值”

        await grain.DoWrite(new List<int> { 1, 2 });
        val = await grain.GetValue();
        Assert.Equal(new List<int> { 1, 2 }, val);  // "写二后的价值"

        val = await grain.DoRead();

        Assert.Equal(new List<int> { 1, 2 }, val);  // 读完后的价值
    }

    [Fact, TestCategory("Functional")]
    public async Task Grain_Generic_GrainStorage_DiffTypes()
    {
        if (!DistinguishesGenericGrainTypeParameters)
        {
            throw Xunit.Sdk.SkipException.ForSkip("This provider does not support distinguishing grains in storage by their generic type parameters.");
        }

        long id1 = Random.Shared.Next();
        var id2 = id1;
        var id3 = id1;

        var grain1 = GrainFactory.GetGrain<IGrainStorageGenericGrain<int>>(id1, grainNamespace);

        var grain2 = GrainFactory.GetGrain<IGrainStorageGenericGrain<string>>(id2, grainNamespace);

        var grain3 = GrainFactory.GetGrain<IGrainStorageGenericGrain<double>>(id3, grainNamespace);

        var val1 = await grain1.GetValue();
        Assert.Equal(0, val1);  // "初始值 - 1"

        var val2 = await grain2.GetValue();
        Assert.Null(val2);  // "初始值 - 2"

        var val3 = await grain3.GetValue();
        Assert.Equal(0.0, val3);  // "初始值 -3"

        var expected1 = 1;
        await grain1.DoWrite(expected1);
        val1 = await grain1.GetValue();
        Assert.Equal(expected1, val1);  // 写完1 -1之后的价值

        var expected2 = "Three";
        await grain2.DoWrite(expected2);
        val2 = await grain2.GetValue();
        Assert.Equal(expected2, val2);  // "写完1 -2之后的价值"

        var expected3 = 5.1;
        await grain3.DoWrite(expected3);
        val3 = await grain3.GetValue();
        Assert.Equal(expected3, val3);  // "写完1 -3后的价值"

        val1 = await grain1.GetValue();
        Assert.Equal(expected1, val1);  // "写2 -1前的价值"
        expected1 = 2;
        await grain1.DoWrite(expected1);
        val1 = await grain1.GetValue();
        Assert.Equal(expected1, val1);  // 写完2 -1后的价值
        val1 = await grain1.DoRead();
        Assert.Equal(expected1, val1);  // 读完后的价值 -1

        val2 = await grain2.GetValue();
        Assert.Equal(expected2, val2);  // "写2 -2前的价值"
        expected2 = "Four";
        await grain2.DoWrite(expected2);
        val2 = await grain2.GetValue();
        Assert.Equal(expected2, val2);  // "写完2 -2之后的价值"
        val2 = await grain2.DoRead();
        Assert.Equal(expected2, val2);  // 读完后的价值 -2

        val3 = await grain3.GetValue();
        Assert.Equal(expected3, val3);  // "写2 -3前的价值"
        expected3 = 6.2;
        await grain3.DoWrite(expected3);
        val3 = await grain3.GetValue();
        Assert.Equal(expected3, val3);  // "写完2 -3后的价值"
        val3 = await grain3.DoRead();
        Assert.Equal(expected3, val3);  // 读完3后的价值
    }

    [Fact, TestCategory("Functional")]
    public async Task Grain_GrainStorage_SiloRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        if (!IsDurableStorage)
        {
            throw Xunit.Sdk.SkipException.ForSkip("This provider does not persist state, so cannot survive a silo restart.");
        }

        var initialServiceId = fixture.GetClientServiceId();

        output.WriteLine("ClusterId={0} ServiceId={1}", HostedCluster.Options.ClusterId, initialServiceId);

        var id1 = Guid.NewGuid();
        var grain1 = GrainFactory.GetGrain<IGrainStorageTestGrain>(id1, grainNamespace);
        var id2 = Guid.NewGuid();
        var grain2 = GrainFactory.GetGrain<IGrainStorageTestGrain>(id2, grainNamespace);

        var val = await grain1.GetValue();
        Assert.Equal(0, val);  // "原价值"

        await grain1.DoWrite(1);

        await grain2.DoWrite(1);

        val = await grain1.GetValue();
        Assert.Equal(1, val);
        val = await grain2.GetValue();
        Assert.Equal(1, val);

        await grain2.DoDelete();

        var serviceId = await GrainFactory.GetGrain<IServiceIdGrain>(Guid.Empty).GetServiceId();
        Assert.Equal(initialServiceId, serviceId);  // "在重新开始之前,ServiceId 是一样的。"

        output.WriteLine("About to reset Silos");
        foreach (var silo in HostedCluster.GetActiveSilos().ToList())
        {
            await HostedCluster.RestartSiloAsync(silo);
        }

        await HostedCluster.InitializeClientAsync(cancellationToken);

        output.WriteLine("Silos restarted");

        serviceId = await GrainFactory.GetGrain<IServiceIdGrain>(Guid.Empty).GetServiceId();
        grain1 = GrainFactory.GetGrain<IGrainStorageTestGrain>(id1, grainNamespace);
        grain2 = GrainFactory.GetGrain<IGrainStorageTestGrain>(id2, grainNamespace);
        output.WriteLine("ClusterId={0} ServiceId={1}", HostedCluster.Options.ClusterId, serviceId);
        Assert.Equal(initialServiceId, serviceId);  // "ServiceId 重新开始后同样如此。”

        val = await grain1.GetValue();
        Assert.Equal(1, val);  // “写后值”

        val = await grain2.GetValue();
        Assert.Equal(0, val);  // 国家应该得到清除。

        await grain1.DoWrite(2);
        val = await grain1.GetValue();
        Assert.Equal(2, val);  // "写二后的价值"

        val = await grain1.DoRead();

        Assert.Equal(2, val);  // 读完后的价值
    }

    [Fact, TestCategory("CorePerf"), TestCategory("Performance"), TestCategory("Stress")]
    public async Task Persistence_Perf_Activate()
    {
        const string testName = "Persistence_Perf_Activate";
        int n = LoopIterations_Grain;
        TimeSpan target = TimeSpan.FromMilliseconds(MaxReadTime * n);

        // 激活的时间
        await RunPerfTest(n, testName, target,
            grainNoState => grainNoState.PingAsync(),
            grainMemory => grainMemory.DoSomething(),
            grainMemoryStore => grainMemoryStore.GetValue(),
            grainAzureStore => grainAzureStore.GetValue());
    }

    [Fact, TestCategory("CorePerf"), TestCategory("Performance"), TestCategory("Stress")]
    public async Task Persistence_Perf_Write()
    {
        const string testName = "Persistence_Perf_Write";
        int n = LoopIterations_Grain;
        TimeSpan target = TimeSpan.FromMilliseconds(MaxWriteTime * n);

        // 写作时间选择
        await RunPerfTest(n, testName, target,
            grainNoState => grainNoState.EchoAsync(testName),
            grainMemory => grainMemory.DoWrite(n),
            grainMemoryStore => grainMemoryStore.DoWrite(n),
            grainAzureStore => grainAzureStore.DoWrite(n));
    }

    [Fact, TestCategory("CorePerf"), TestCategory("Performance"), TestCategory("Stress")]
    public async Task Persistence_Perf_Write_Reread()
    {
        const string testName = "Persistence_Perf_Write_Read";
        int n = LoopIterations_Grain;
        TimeSpan target = TimeSpan.FromMilliseconds(MaxWriteTime * n);

        // 写作时间选择
        await RunPerfTest(n, testName + "--Write", target,
            grainNoState => grainNoState.EchoAsync(testName),
            grainMemory => grainMemory.DoWrite(n),
            grainMemoryStore => grainMemoryStore.DoWrite(n),
            grainAzureStore => grainAzureStore.DoWrite(n));

        // 激活的时间
        await RunPerfTest(n, testName + "--ReRead", target,
            grainNoState => grainNoState.GetLastEchoAsync(),
            grainMemory => grainMemory.DoRead(),
            grainMemoryStore => grainMemoryStore.DoRead(),
            grainAzureStore => grainAzureStore.DoRead());
    }

    protected async Task Persistence_Silo_StorageProvider(string providerName)
    {
        List<SiloHandle> silos = this.HostedCluster.GetActiveSilos().ToList();
        foreach (var silo in silos)
        {
            var isPresent = await this.HostedCluster.Client!.GetTestHooks(silo).HasStorageProvider(providerName);
            Assert.True(isPresent, $"No storage provider found: {providerName}");
        }
    }

    // - 效用功能...

    private async Task RunPerfTest(int n, string testName, TimeSpan target,
        Func<IEchoTaskGrain, Task> actionNoState,
        Func<IPersistenceTestGrain, Task> actionMemory,
        Func<IMemoryStorageTestGrain, Task> actionMemoryStore,
        Func<IGrainStorageTestGrain, Task> actionAzureTable)
    {
        IEchoTaskGrain[] noStateGrains = new IEchoTaskGrain[n];
        IPersistenceTestGrain[] memoryGrains = new IPersistenceTestGrain[n];
        IGrainStorageTestGrain[] azureStoreGrains = new IGrainStorageTestGrain[n];
        IMemoryStorageTestGrain[] memoryStoreGrains = new IMemoryStorageTestGrain[n];

        for (int i = 0; i < n; i++)
        {
            Guid id = Guid.NewGuid();
            noStateGrains[i] = this.GrainFactory.GetGrain<IEchoTaskGrain>(id);
            memoryGrains[i] = this.GrainFactory.GetGrain<IPersistenceTestGrain>(id);
            azureStoreGrains[i] = this.GrainFactory.GetGrain<IGrainStorageTestGrain>(id);
            memoryStoreGrains[i] = this.GrainFactory.GetGrain<IMemoryStorageTestGrain>(id);
        }

        TimeSpan baseline, elapsed;

        elapsed = baseline = await TestUtils.TimeRunAsync(n, TimeSpan.Zero, testName + " (No state)",
            () => RunIterations(testName, n, i => actionNoState(noStateGrains[i])));

        elapsed = await TestUtils.TimeRunAsync(n, baseline, testName + " (Local Memory Store)",
            () => RunIterations(testName, n, i => actionMemory(memoryGrains[i])));

        elapsed = await TestUtils.TimeRunAsync(n, baseline, testName + " (Dev Store Grain Store)",
            () => RunIterations(testName, n, i => actionMemoryStore(memoryStoreGrains[i])));

        elapsed = await TestUtils.TimeRunAsync(n, baseline, testName + " (Azure Table Store)",
            () => RunIterations(testName, n, i => actionAzureTable(azureStoreGrains[i])));

        if (elapsed > target.Multiply(timingFactor))
        {
            string msg = string.Format("{0}: Elapsed time {1} exceeds target time {2}", testName, elapsed, target);

            if (elapsed > target.Multiply(2.0 * timingFactor))
            {
                Assert.Fail(msg);
            }
            else
            {
                throw Xunit.Sdk.SkipException.ForSkip(msg);
            }
        }
    }

    private async Task RunIterations(string testName, int n, Func<int, Task> action)
    {
        List<Task> promises = new List<Task>();
        Stopwatch sw = Stopwatch.StartNew();
        // 分批解雇请求
        for (int i = 0; i < n; i++)
        {
            var promise = action(i);
            promises.Add(promise);
            if ((i % BatchSize) == 0 && i > 0)
            {
                await Task.WhenAll([.. promises]).WaitAsync(PerformanceTestTimeout);
                promises.Clear();
                //output.WriteLine("{{{0} 已经做了{1} 在{2} 在{3} RPS的 {3} 迭代,"
                //                  testName, i, sw.Elapsed, i / sw.Elapsed.TotalSeconds);
            }
        }

        await Task.WhenAll([.. promises]).WaitAsync(PerformanceTestTimeout);
        sw.Stop();
        output.WriteLine("{0} completed. Did {1} iterations in {2} at {3} RPS",
                          testName, n, sw.Elapsed, n / sw.Elapsed.TotalSeconds);
    }
}
