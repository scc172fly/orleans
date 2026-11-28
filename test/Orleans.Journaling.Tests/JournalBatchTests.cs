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
/// 用于测试日志存储实现的基类。
/// 派生类必须实现 <see cref="ConfigureServices"/> 以注册特定的存储工厂。
/// 此类提供一组通用测试，用于验证 <see cref="DurableList{T}"/>
/// 在不同存储后端上的行为。
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
    /// 测试基本的 Add、Update（按索引）及 RemoveAt 操作。
    /// </summary>
    [Fact]
    public async Task DurableList_BasicOperations_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var grainId = GrainId.Create("test-grain", $"basicList-{Guid.NewGuid()}"); // 此次测试运行的唯一 ID
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
    /// 测试列表状态能否正确持久化并由新实例恢复。
    /// </summary>
    [Fact]
    public async Task DurableList_Persistence_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var listName = "persistenceList";
        var grainId = GrainId.Create("test-grain", $"{listName}-{Guid.NewGuid()}"); // 用于恢复的同一 GrainId
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
    /// 测试复杂对象的存储和检索，包括对可变属性的更新。
    /// </summary>
    [Fact]
    public async Task DurableList_ComplexValues_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var listName = "personList";
        var grainId = GrainId.Create("test-grain", $"{listName}-{Guid.NewGuid()}"); // 用于恢复的同一 GrainId
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

        // 重新读取以确认变更已持久化
        var (manager2, list2, _) = CreateTestComponents<TestPerson>(listName, grainId); // 使用同一 GrainId 重新加载
        await manager2.InitializeAsync(cts.Token);
        Assert.Equal(31, list2[0].Age);
    }

    /// <summary>
    /// 测试 Clear 操作及其持久化。
    /// </summary>
    [Fact]
    public async Task DurableList_Clear_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var listName = "clearList";
        var grainId = GrainId.Create("test-grain", $"{listName}-{Guid.NewGuid()}"); // 用于恢复的同一 GrainId
        var (manager, list, _) = CreateTestComponents<string>(listName, grainId);
        await manager.InitializeAsync(cts.Token);

        list.Add("one");
        list.Add("two");
        list.Add("three");
        await manager.WriteStateAsync(cts.Token);

        list.Clear();
        await manager.WriteStateAsync(cts.Token);

        Assert.Empty(list);

        // 验证 Clear 操作的持久化
        var (manager2, list2, _) = CreateTestComponents<string>(listName, grainId); // 使用同一 GrainId 重新加载
        await manager2.InitializeAsync(cts.Token);
        Assert.Empty(list2);
    }

    /// <summary>
    /// 测试 Contains 方法及 Remove（按值）操作。
    /// </summary>
    [Fact]
    public async Task DurableList_Contains_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var grainId = GrainId.Create("test-grain", $"containsList-{Guid.NewGuid()}"); // 此次测试运行的唯一 ID
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
    /// 测试 Insert 及 Remove（按值）操作。
    /// </summary>
    [Fact]
    public async Task DurableList_InsertAndRemove_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var grainId = GrainId.Create("test-grain", $"insertList-{Guid.NewGuid()}"); // 此次测试运行的唯一 ID
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
    /// 测试使用 ToList()（依赖 GetEnumerator）枚举列表。
    /// </summary>
    [Fact]
    public async Task DurableList_Enumeration_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var grainId = GrainId.Create("test-grain", $"enumList-{Guid.NewGuid()}"); // 此次测试运行的唯一 ID
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
    /// 测试大量操作（添加、更新）及多次写入时的行为，
    /// 这些操作可能触发存储提供程序的快照生成行为。同时测试恢复。
    /// </summary>
    [Fact]
    public async Task DurableList_LargeNumberOfOperations_And_Snapshot_Test()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)); // 延长超时时间
        var listName = "largeList";
        var grainId = GrainId.Create("test-grain", $"{listName}-{Guid.NewGuid()}"); // 用于恢复的同一 GrainId
        var (manager, list, storage) = CreateTestComponents<int>(listName, grainId);
        await manager.InitializeAsync(cts.Token);

        const int itemCount = 100; // 减少数量以加快测试速度，必要时可增加
        for (int i = 0; i < itemCount; i++)
        {
            list.Add(i);
        }

        // 多次写入以尝试触发快照生成
        for (int j = 0; j < 5; ++j)
        {
            await manager.WriteStateAsync(cts.Token);
        }

        Assert.Equal(itemCount, list.Count);

        for (int i = 0; i < itemCount; i += 2)
        {
            list[i] = list[i] * 2;
        }

        // 再次进行多次写入
        for (int j = 0; j < 5; ++j)
        {
            await manager.WriteStateAsync(cts.Token);
        }

        for (int i = 0; i < itemCount; i++)
        {
            if (i % 2 == 0) Assert.Equal(i * 2, list[i]);
            else Assert.Equal(i, list[i]);
        }

        // 测试恢复（可能从快照恢复）
        var sessionPool = _serviceProvider.GetRequiredService<SerializerSessionPool>();
        var codecProvider = _serviceProvider.GetRequiredService<ICodecProvider>();
        var manager2 = new JournaledStateManager(CreateShared(), storage); // 复用通过 grainId 关联的存储对象
        var list2 = new DurableList<int>(listName, manager2, new OrleansBinaryDurableListCommandCodec<int>(codecProvider.GetCodec<int>(), sessionPool));
        await manager2.InitializeAsync(cts.Token);

        Assert.Equal(itemCount, list2.Count);
        for (int i = 0; i < itemCount; i++)
        {
            if (i % 2 == 0) Assert.Equal(i * 2, list2[i]);
            else Assert.Equal(i, list2[i]);
        }
    }

    // 保留 TestGrainContext，并添加其中一个测试所需的 TestPerson 记录类型
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
