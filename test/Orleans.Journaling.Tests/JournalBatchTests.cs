using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration.Internal;
using Orleans.Serialization;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Session;
using TestExtensions;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

[TestSuite("Functional")]
[TestProvider("AzureStorage")]
[TestCategory("AzureStorage"), TestCategory("Functional")]
public sealed class AzureStorageJournalBatchTests : JournalBatchTests
{
    public AzureStorageJournalBatchTests()
    {
        JournalingAzureStorageTestConfiguration.CheckPreconditionsOrThrow();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.Configure<AzureBlobJournalStorageOptions>(options => JournalingAzureStorageTestConfiguration.ConfigureTestDefaults(options));
        services.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = OrleansBinaryJournalFormat.JournalFormatKey);
        services.AddSingleton<AzureBlobJournalStorageProvider>();
        services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, AzureBlobJournalStorageProvider>();
    }

    protected override IJournalStorage CreateStorage(IServiceProvider serviceProvider, IGrainContext grainContext) =>
        serviceProvider.GetRequiredService<AzureBlobJournalStorageProvider>()
            .CreateStorage(JournalId.FromGrainId(grainContext.GrainId));
}

public sealed class InMemoryJournalBatchTests : JournalBatchTests
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<VolatileJournalStorageProvider>();
    }

    protected override IJournalStorage CreateStorage(IServiceProvider serviceProvider, IGrainContext grainContext) =>
        serviceProvider.GetRequiredService<VolatileJournalStorageProvider>()
            .CreateStorage(JournalId.FromGrainId(grainContext.GrainId));
}

/// <summary>
/// 测试日记存储实施情况的基础等级。
/// 衍生类别必须实施 <see cref="ConfigureServices"/> ,以登记具体的储存厂。
/// 此类为验证 <see cref="DurableList{T}"/>的行为提供一套通用测试
/// 相对于不同的存储后端。
/// </summary>
public abstract class JournalBatchTests : IAsyncLifetime
{
    private IServiceProvider _serviceProvider = null!;
    private SiloLifecycleSubject? _siloLifecycle;
    private static readonly IOptions<JournaledStateManagerOptions> ManagerOptions = Options.Create(new JournaledStateManagerOptions
    {
        JournalFormatKey = OrleansBinaryJournalFormat.JournalFormatKey
    });

    private JournaledStateManagerShared CreateShared()
        => new(
            _serviceProvider.GetRequiredService<ILogger<JournaledStateManager>>(),
            ManagerOptions,
            TimeProvider.System,
            _serviceProvider);

    public virtual async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddSerializer();
        services.AddLogging();
        services.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = OrleansBinaryJournalFormat.JournalFormatKey);
        ConfigureBinaryJournalingServices(services);
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        _siloLifecycle = new SiloLifecycleSubject(_serviceProvider.GetRequiredService<ILogger<SiloLifecycleSubject>>());
        var participants = _serviceProvider.GetServices<ILifecycleParticipant<ISiloLifecycle>>();
        foreach (var participant in participants)
        {
            participant.Participate(_siloLifecycle);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await _siloLifecycle.OnStart(cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_siloLifecycle is not null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await _siloLifecycle.OnStop(cts.Token);
        }
    }

    protected abstract void ConfigureServices(IServiceCollection services);

    protected abstract IJournalStorage CreateStorage(IServiceProvider serviceProvider, IGrainContext grainContext);

    private static void ConfigureBinaryJournalingServices(IServiceCollection services)
    {
        services.AddSingleton<OrleansBinaryJournalFormat>();
        services.AddKeyedSingleton<IJournalFormat>(
            OrleansBinaryJournalFormat.JournalFormatKey,
            static (sp, _) => sp.GetRequiredService<OrleansBinaryJournalFormat>());
        services.AddKeyedSingleton(
            typeof(IDurableDictionaryCommandCodec<,>),
            OrleansBinaryJournalFormat.JournalFormatKey,
            typeof(OrleansBinaryDurableDictionaryCommandCodec<,>));
    }

    private (JournaledStateManager Manager, DurableList<T> List, IJournalStorage Storage) CreateTestComponents<T>(string listName, GrainId grainId)
    {
        var sessionPool = _serviceProvider.GetRequiredService<SerializerSessionPool>();
        var codecProvider = _serviceProvider.GetRequiredService<ICodecProvider>();
        var grainContext = new TestGrainContext(grainId, _serviceProvider);
        var storage = CreateStorage(_serviceProvider, grainContext);
        var manager = new JournaledStateManager(CreateShared(), storage);
        var list = new DurableList<T>(listName, manager, new OrleansBinaryDurableListCommandCodec<T>(codecProvider.GetCodec<T>(), sessionPool));
        return (manager, list, storage);
    }

    /// <summary>
    /// 测试基本添加、更新(按指数)和 RemoveAt 操作。
    /// </summary>
    [Fact]
    public async Task DurableList_BasicOperations_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var grainId = GrainId.Create("test-grain", $"basicList-{Guid.NewGuid()}"); // 此测试运行的唯一身份标识
        var (manager, list, _) = CreateTestComponents<string>("basicList", grainId);
        await manager.InitializeAsync(cts.Token);

        list.Add("one");
        list.Add("two");
        list.Add("three");
        await manager.WriteStateAsync(cts.Token);

        Assert.Equal(3, list.Count);
        Assert.Equal("one", list[0]);
        Assert.Equal("two", list[1]);
        Assert.Equal("three", list[2]);

        list[1] = "updated";
        await manager.WriteStateAsync(cts.Token);

        Assert.Equal("updated", list[1]);

        list.RemoveAt(0);
        await manager.WriteStateAsync(cts.Token);

        Assert.Equal(2, list.Count);
        Assert.Equal("updated", list[0]);
        Assert.Equal("three", list[1]);
    }

    /// <summary>
    /// 列表状态的测试正确持续,可以通过新实例恢复。
    /// </summary>
    [Fact]
    public async Task DurableList_Persistence_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var listName = "persistenceList";
        var grainId = GrainId.Create("test-grain", $"{listName}-{Guid.NewGuid()}"); // 恢复的一致 GrainId
        var (manager1, list1, storage) = CreateTestComponents<string>(listName, grainId);
        await manager1.InitializeAsync(cts.Token);

        list1.Add("one");
        list1.Add("two");
        list1.Add("three");
        await manager1.WriteStateAsync(cts.Token);

        var sessionPool = _serviceProvider.GetRequiredService<SerializerSessionPool>();
        var codecProvider = _serviceProvider.GetRequiredService<ICodecProvider>();
        var manager2 = new JournaledStateManager(CreateShared(), storage);
        var list2 = new DurableList<string>(listName, manager2, new OrleansBinaryDurableListCommandCodec<string>(codecProvider.GetCodec<string>(), sessionPool));
        await manager2.InitializeAsync(cts.Token);

        Assert.Equal(3, list2.Count);
        Assert.Equal("one", list2[0]);
        Assert.Equal("two", list2[1]);
        Assert.Equal("three", list2[2]);
    }

    /// <summary>
    /// 试验储存和检索复杂物体,包括更新变异特性。
    /// </summary>
    [Fact]
    public async Task DurableList_ComplexValues_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var listName = "personList";
        var grainId = GrainId.Create("test-grain", $"{listName}-{Guid.NewGuid()}"); // 恢复的一致 GrainId
        var (manager, list, _) = CreateTestComponents<TestPerson>(listName, grainId);
        await manager.InitializeAsync(cts.Token);

        var person1 = new TestPerson { Id = 1, Name = "John", Age = 30 };
        var person2 = new TestPerson { Id = 2, Name = "Jane", Age = 25 };

        list.Add(person1);
        list.Add(person2);
        await manager.WriteStateAsync(cts.Token);

        Assert.Equal(2, list.Count);
        Assert.Equal("John", list[0].Name);
        Assert.Equal(25, list[1].Age);

        list[0] = list[0] with { Age = 31 };
        await manager.WriteStateAsync(cts.Token);

        // 重新阅读以确认变化的持久性
        var (manager2, list2, _) = CreateTestComponents<TestPerson>(listName, grainId); // 使用相同的 GrainId 重新装入
        await manager2.InitializeAsync(cts.Token);
        Assert.Equal(31, list2[0].Age);
    }

    /// <summary>
    /// 测试清除操作及其持久性。
    /// </summary>
    [Fact]
    public async Task DurableList_Clear_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var listName = "clearList";
        var grainId = GrainId.Create("test-grain", $"{listName}-{Guid.NewGuid()}"); // 恢复的一致 GrainId
        var (manager, list, _) = CreateTestComponents<string>(listName, grainId);
        await manager.InitializeAsync(cts.Token);

        list.Add("one");
        list.Add("two");
        list.Add("three");
        await manager.WriteStateAsync(cts.Token);

        list.Clear();
        await manager.WriteStateAsync(cts.Token);

        Assert.Empty(list);

        // 清除的核查持久性
        var (manager2, list2, _) = CreateTestComponents<string>(listName, grainId); // 使用相同的 GrainId 重新装入
        await manager2.InitializeAsync(cts.Token);
        Assert.Empty(list2);
    }

    /// <summary>
    /// 测试“包含”方法和删除(按值)操作。
    /// </summary>
    [Fact]
    public async Task DurableList_Contains_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var grainId = GrainId.Create("test-grain", $"containsList-{Guid.NewGuid()}"); // 此测试运行的唯一身份标识
        var (manager, list, _) = CreateTestComponents<string>("containsList", grainId);
        await manager.InitializeAsync(cts.Token);

        list.Add("one");
        list.Add("two");
        list.Add("three");
        await manager.WriteStateAsync(cts.Token);

        Assert.Contains("two", list);
        Assert.DoesNotContain("four", list);

        list.Remove("two");
        await manager.WriteStateAsync(cts.Token);

        Assert.DoesNotContain("two", list);
    }

    /// <summary>
    /// 插入和删除(按值)操作。
    /// </summary>
    [Fact]
    public async Task DurableList_InsertAndRemove_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var grainId = GrainId.Create("test-grain", $"insertList-{Guid.NewGuid()}"); // 此测试运行的唯一身份标识
        var (manager, list, _) = CreateTestComponents<string>("insertList", grainId);
        await manager.InitializeAsync(cts.Token);

        list.Add("one");
        list.Add("three");
        await manager.WriteStateAsync(cts.Token);

        list.Insert(1, "two");
        await manager.WriteStateAsync(cts.Token);

        Assert.Equal(3, list.Count);
        Assert.Equal("one", list[0]);
        Assert.Equal("two", list[1]);
        Assert.Equal("three", list[2]);

        bool removed = list.Remove("two");
        await manager.WriteStateAsync(cts.Token);

        Assert.True(removed);
        Assert.Equal(2, list.Count);
        Assert.Equal("one", list[0]);
        Assert.Equal("three", list[1]);
    }

    /// <summary>
    /// 使用 ToList() ((依赖 GetEnumerator) 进行测试列表查点。
    /// </summary>
    [Fact]
    public async Task DurableList_Enumeration_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var grainId = GrainId.Create("test-grain", $"enumList-{Guid.NewGuid()}"); // 此测试运行的唯一身份标识
        var (manager, list, _) = CreateTestComponents<string>("enumList", grainId);
        await manager.InitializeAsync(cts.Token);

        var expectedItems = new List<string> { "one", "two", "three" };

        foreach (var item in expectedItems)
        {
            list.Add(item);
        }

        await manager.WriteStateAsync(cts.Token);

        var actualItems = list.ToList();

        Assert.Equal(expectedItems, actualItems);
    }

    /// <summary>
    /// 测试行为,
    /// 可能引发存储供应商的即时行为 还有测试回收
    /// </summary>
    [Fact]
    public async Task DurableList_LargeNumberOfOperations_And_Snapshot_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)); // 超时时间增加
        var listName = "largeList";
        var grainId = GrainId.Create("test-grain", $"{listName}-{Guid.NewGuid()}"); // 恢复的一致 GrainId
        var (manager, list, storage) = CreateTestComponents<int>(listName, grainId);
        await manager.InitializeAsync(cts.Token);

        const int itemCount = 100; // 减少速度更快的测试,必要时增加
        for (int i = 0; i < itemCount; i++)
        {
            list.Add(i);
        }

        // 多次写信,以触发快速抓捕
        for (int j = 0; j < 5; ++j)
        {
            await manager.WriteStateAsync(cts.Token);
        }

        Assert.Equal(itemCount, list.Count);

        for (int i = 0; i < itemCount; i += 2)
        {
            list[i] = list[i] * 2;
        }

        // 重复写多次
        for (int j = 0; j < 5; ++j)
        {
            await manager.WriteStateAsync(cts.Token);
        }

        for (int i = 0; i < itemCount; i++)
        {
            if (i % 2 == 0) Assert.Equal(i * 2, list[i]);
            else Assert.Equal(i, list[i]);
        }

        // 测试恢复(可能从快照中)
        var sessionPool = _serviceProvider.GetRequiredService<SerializerSessionPool>();
        var codecProvider = _serviceProvider.GetRequiredService<ICodecProvider>();
        var manager2 = new JournaledStateManager(CreateShared(), storage); // 重新使用通过 grainId连接的存储对象
        var list2 = new DurableList<int>(listName, manager2, new OrleansBinaryDurableListCommandCodec<int>(codecProvider.GetCodec<int>(), sessionPool));
        await manager2.InitializeAsync(cts.Token);

        Assert.Equal(itemCount, list2.Count);
        for (int i = 0; i < itemCount; i++)
        {
            if (i % 2 == 0) Assert.Equal(i * 2, list2[i]);
            else Assert.Equal(i, list2[i]);
        }
    }

    // 保留 TestGrainContext 并添加其中一项试验所需的 TestPerson 记录
    [GenerateSerializer, Immutable]
    internal sealed record TestPerson
    {
        [Id(0)]
        public int Id { get; init; }
        [Id(1)]
        public string Name { get; init; } = "";
        [Id(2)]
        public int Age { get; init; }
    }

    internal sealed class TestGrainContext(GrainId grainId, IServiceProvider services) : IGrainContext
    {
        private readonly Dictionary<Type, object> _components = [];

        public GrainReference GrainReference => throw new NotImplementedException();
        public GrainId GrainId => grainId;
        public object? GrainInstance => throw new NotImplementedException();
        public ActivationId ActivationId => throw new NotImplementedException();
        public GrainAddress Address => throw new NotImplementedException();
        public IServiceProvider ActivationServices => services;
        public IGrainLifecycle ObservableLifecycle { get; } = new CompositionTestLifecycle();
        public IWorkItemScheduler Scheduler => throw new NotImplementedException();
        public Task Deactivated => throw new NotImplementedException();

        public void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public void Deactivate(DeactivationReason deactivationReason, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public bool Equals(IGrainContext? other) => throw new NotImplementedException();
        public TComponent? GetComponent<TComponent>() where TComponent : class => (TComponent?)GetComponent(typeof(TComponent));
        public object? GetComponent(Type componentType) => _components.GetValueOrDefault(componentType);
        public TTarget? GetTarget<TTarget>() where TTarget : class => throw new NotImplementedException();
        public object? GetTarget() => throw new NotImplementedException();
        public void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public void ReceiveMessage(object message) => throw new NotImplementedException();
        public void Rehydrate(IRehydrationContext context) => throw new NotImplementedException();
        public void SetComponent<TComponent>(TComponent? value) where TComponent : class
        {
            if (value is null)
            {
                _components.Remove(typeof(TComponent));
            }
            else
            {
                _components[typeof(TComponent)] = value;
            }
        }
    }
}
