using Orleans.Core.Internal;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// Orleans 中 DurableGrain 功能的集成测试。
/// 
/// DurableGrain 是一个基类，利用 Orleans 的日志化
/// 基础设施自动持久化状态，使 Grain 状态能在 Grain 停用或节点故障后恢复，并
/// 通过将所有状态修改记入持久化日志来应对系统重启。
/// 
/// 这些测试验证以下内容：
/// - 跨 Grain 激活实例的状态持久化
/// - 对复杂类型和集合的支持
/// - 同一个 Grain 中存在多个持久化集合
/// - 大规模状态的处理
/// </summary>
[TestCategory("BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Journaling")]
public class DurableGrainTests(IntegrationTestFixture fixture) : IClassFixture<IntegrationTestFixture>
{
    private IGrainFactory Client => fixture.Client;

    /// <summary>
    /// 测试持久化 Grain 的基本状态持久化功能。
    /// 验证简单状态属性（string 和 int）可以正确
    /// 持久化，并在 Grain 停用后恢复。
    /// </summary>
    [Fact]
    public async Task DurableGrain_State_Persistence_Test()
    {
        // 准备
        var grain = Client.GetGrain<ITestDurableGrain>(Guid.NewGuid());

        // 执行（Act）：设置状态属性并持久化。
        await grain.SetTestValues("Test Name", 42);

        // 验证
        Assert.Equal("Test Name", await grain.GetName());
        Assert.Equal(42, await grain.GetCounter());

        // 强制停用，并获取新的引用。
        // 模拟 Grain 故障或节点重启场景。
        var idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 验证（Assert）：应恢复状态。
        Assert.Equal("Test Name", await grain.GetName());
        Assert.Equal(42, await grain.GetCounter());
    }

    /// <summary>
    /// 测试状态更新能否正确记入日志并持久化。
    /// 验证 Grain 停用后恢复的是最新的状态值，
    /// 而不是初始值。
    /// </summary>
    [Fact]
    public async Task DurableGrain_Update_State_Test()
    {
        // 准备
        var grain = Client.GetGrain<ITestDurableGrain>(Guid.NewGuid());

        // 执行（Act）：设置状态并持久化。
        await grain.SetTestValues("Initial Name", 10);

        // 更新状态并再次持久化。
        await grain.SetTestValues("Updated Name", 20);

        // 验证
        Assert.Equal("Updated Name", await grain.GetName());
        Assert.Equal(20, await grain.GetCounter());

        // 强制停用，并获取新的引用。
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);

        // 验证（Assert）：应恢复更新后的状态。
        Assert.Equal("Updated Name", await grain.GetName());
        Assert.Equal(20, await grain.GetCounter());
    }

    /// <summary>
    /// 测试复杂类型的持久化，包括自定义对象和集合。
    /// 验证 Orleans 的序列化机制能否正确处理持久化 Grain 状态中的
    /// 嵌套对象和集合。
    /// </summary>
    [Fact]
    public async Task DurableGrain_Complex_Types_Test()
    {
        // 准备
        var grain = Client.GetGrain<ITestDurableGrainWithComplexState>(Guid.NewGuid());

        // 执行（Act）：设置复杂状态并持久化。
        var person = new TestPerson { Id = 1, Name = "John Doe", Age = 30 };
        var items = new List<string> { "Item1", "Item2", "Item3" };
        await grain.SetTestValues(person, items);

        // 验证
        var retrievedPerson = await grain.GetPerson();
        var retrievedItems = await grain.GetItems();

        Assert.Equal("John Doe", retrievedPerson.Name);
        Assert.Equal(3, retrievedItems.Count);

        // 强制停用，并获取新的引用。
        // 模拟 Grain 故障或节点重启场景。
        var idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 验证（Assert）：应恢复复杂状态。
        retrievedPerson = await grain.GetPerson();
        retrievedItems = await grain.GetItems();

        Assert.NotNull(retrievedPerson);
        Assert.Equal(1, retrievedPerson.Id);
        Assert.Equal("John Doe", retrievedPerson.Name);
        Assert.Equal(30, retrievedPerson.Age);

        Assert.Equal(3, retrievedItems.Count);
        Assert.Equal("Item1", retrievedItems[0]);
        Assert.Equal("Item2", retrievedItems[1]);
        Assert.Equal("Item3", retrievedItems[2]);
    }

    /// <summary>
    /// 测试同时使用多个持久化集合的 Grain。
    /// 验证 DurableDictionary、DurableList、DurableQueue 和 DurableSet
    /// 能否在同一个 Grain 中共存，并各自独立维护状态。
    /// </summary>
    [Fact]
    public async Task DurableGrain_Multiple_Collections_Test()
    {
        // 准备
        var grain = Client.GetGrain<ITestMultiCollectionGrain>(Guid.NewGuid());

        // 执行（Act）：填充集合并持久化。
        await grain.AddToDictionary("key1", 1);
        await grain.AddToDictionary("key2", 2);
        await grain.AddToList("item1");
        await grain.AddToList("item2");
        await grain.AddToQueue(100);
        await grain.AddToQueue(200);
        await grain.AddToSet("set1");
        await grain.AddToSet("set2");

        // 验证
        Assert.Equal(2, await grain.GetDictionaryCount());
        Assert.Equal(2, await grain.GetListCount());
        Assert.Equal(2, await grain.GetQueueCount());
        Assert.Equal(2, await grain.GetSetCount());

        // 强制停用，并获取新的引用。
        // 模拟 Grain 故障或节点重启场景。
        var idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 验证（Assert）：应恢复所有集合。
        Assert.Equal(2, await grain.GetDictionaryCount());
        Assert.Equal(1, await grain.GetDictionaryValue("key1"));
        Assert.Equal(2, await grain.GetDictionaryValue("key2"));

        Assert.Equal(2, await grain.GetListCount());
        Assert.Equal("item1", await grain.GetListItem(0));
        Assert.Equal("item2", await grain.GetListItem(1));

        Assert.Equal(2, await grain.GetQueueCount());
        Assert.Equal(100, await grain.PeekQueueItem());

        Assert.Equal(2, await grain.GetSetCount());
        Assert.True(await grain.ContainsSetItem("set1"));
        Assert.True(await grain.ContainsSetItem("set2"));
    }

    /// <summary>
    /// 测试包含更新和删除的复杂状态修改场景。
    /// 验证所有修改操作（添加、更新、删除）均正确
    /// 记入日志，并能正确恢复最终状态。
    /// </summary>
    [Fact]
    public async Task DurableGrain_State_Modifications_Test()
    {
        // 准备
        var grain = Client.GetGrain<ITestMultiCollectionGrain>(Guid.NewGuid());

        // 执行（Act）：填充初始状态并持久化。
        await grain.AddToDictionary("key1", 1);
        await grain.AddToList("item1");
        await grain.AddToQueue(100);
        await grain.AddToSet("set1");

        // 修改状态并再次持久化。
        await grain.AddToDictionary("key2", 2);
        await grain.AddToDictionary("key1", 10); // 通过接口方法更新。
        await grain.AddToList("item2");
        await grain.AddToQueue(200);
        await grain.AddToSet("set2");

        // 验证
        Assert.Equal(2, await grain.GetDictionaryCount());
        Assert.Equal(10, await grain.GetDictionaryValue("key1"));
        Assert.Equal(2, await grain.GetListCount());
        Assert.Equal(2, await grain.GetQueueCount());
        Assert.Equal(2, await grain.GetSetCount());

        // 强制停用，并获取新的引用。
        // 模拟 Grain 故障或节点重启场景。
        var idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 验证（Assert）：应恢复修改后的状态。
        Assert.Equal(2, await grain.GetDictionaryCount());
        Assert.Equal(10, await grain.GetDictionaryValue("key1"));
        Assert.Equal(2, await grain.GetDictionaryValue("key2"));

        Assert.Equal(2, await grain.GetListCount());
        Assert.Equal("item1", await grain.GetListItem(0));
        Assert.Equal("item2", await grain.GetListItem(1));

        // 进一步修改状态。
        await grain.RemoveFromDictionary("key1");
        await grain.RemoveListItemAt(0);
        await grain.DequeueItem();
        await grain.RemoveFromSet("set1");

        // 验证这些修改。
        Assert.Equal(1, await grain.GetDictionaryCount());
        Assert.Equal(1, await grain.GetListCount());
        Assert.Equal(1, await grain.GetQueueCount());
        Assert.Equal(1, await grain.GetSetCount());
    }

    /// <summary>
    /// 测试使用另一种 Grain 接口进行 Grain 状态持久化。
    /// 此测试使用更简单的 Grain 接口
    /// 对持久化机制进行额外验证。
    /// </summary>
    [Fact]
    public async Task Grain_State_Should_Persist_Between_Activations()
    {
        // 准备（Arrange）：获取 Grain 引用。
        var grain = Client.GetGrain<ITestDurableGrainInterface>(Guid.NewGuid());

        // 执行（Act）：设置 Grain 状态。
        await grain.SetValues("Test Name", 42);
        var initialState = await grain.GetValues();

        // 强制停用 Grain。
        var idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 从 Grain 获取值（Grain 将重新激活）。
        var newState = await grain.GetValues();

        // 验证
        Assert.Equal(initialState.Name, newState.Name);
        Assert.Equal(initialState.Counter, newState.Counter);
    }

    /// <summary>
    /// 对多种集合操作和持久化进行综合测试。
    /// 测试所有集合类型的添加、删除和查询操作，
    /// 验证多次停用周期中的状态一致性。
    /// </summary>
    [Fact]
    public async Task Grain_Should_Handle_Multiple_Collections()
    {
        // 准备
        var grain = Client.GetGrain<ITestMultiCollectionGrain>(Guid.NewGuid());

        // 执行（Act）：向集合添加条目。
        await grain.AddToDictionary("key1", 1);
        await grain.AddToDictionary("key2", 2);

        await grain.AddToList("item1");
        await grain.AddToList("item2");

        await grain.AddToQueue(100);
        await grain.AddToQueue(200);

        await grain.AddToSet("set1");
        await grain.AddToSet("set2");
        await grain.AddToSet("set1"); // 重复条目，应被忽略。

        // 验证（Assert）：检查条目数量。
        Assert.Equal(2, await grain.GetDictionaryCount());
        Assert.Equal(2, await grain.GetListCount());
        Assert.Equal(2, await grain.GetQueueCount());
        Assert.Equal(2, await grain.GetSetCount());

        // 强制停用 Grain。
        var idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 验证（Assert）：检查重新激活后的值。
        Assert.Equal(1, await grain.GetDictionaryValue("key1"));
        Assert.Equal(2, await grain.GetDictionaryValue("key2"));
        Assert.Equal("item1", await grain.GetListItem(0));
        Assert.Equal("item2", await grain.GetListItem(1));
        Assert.Equal(100, await grain.PeekQueueItem());
        Assert.True(await grain.ContainsSetItem("set1"));
        Assert.True(await grain.ContainsSetItem("set2"));

        // 执行（Act）：修改集合。
        await grain.RemoveFromDictionary("key1");
        await grain.RemoveListItemAt(0);
        await grain.DequeueItem();
        await grain.RemoveFromSet("set1");

        // 验证（Assert）：检查修改后的条目数量。
        Assert.Equal(1, await grain.GetDictionaryCount());
        Assert.Equal(1, await grain.GetListCount());
        Assert.Equal(1, await grain.GetQueueCount());
        Assert.Equal(1, await grain.GetSetCount());

        // 再次停用 Grain。
        idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 验证（Assert）：检查第二次重新激活后的值。
        Assert.Equal(1, await grain.GetDictionaryCount());
        Assert.Equal(1, await grain.GetListCount());
        Assert.Equal(1, await grain.GetQueueCount());
        Assert.Equal(1, await grain.GetSetCount());
        Assert.Equal(2, await grain.GetDictionaryValue("key2"));
        Assert.Equal("item2", await grain.GetListItem(0));
        Assert.Equal(200, await grain.PeekQueueItem());
        Assert.True(await grain.ContainsSetItem("set2"));
    }

    /// <summary>
    /// 持久化 Grain 大规模状态处理的压力测试。
    /// 验证日志化系统能否处理具有数千个状态条目的 Grain，
    /// 而不发生性能下降或数据丢失。
    /// </summary>
    [Fact]
    public async Task Grain_Should_Handle_Large_State()
    {
        // 准备
        var grain = Client.GetGrain<ITestMultiCollectionGrain>(Guid.NewGuid());

        // 执行（Act）：添加大量条目。
        const int itemCount = 1000;
        for (int i = 0; i < itemCount; i++)
        {
            await grain.AddToDictionary($"key{i}", i);
            if (i < 100) // 向其他集合添加较少的条目，以将测试运行时间保持在合理范围内。
            {
                await grain.AddToList($"item{i}");
                await grain.AddToQueue(i);
                await grain.AddToSet($"set{i}");
            }
        }

        // 验证（Assert）：检查条目数量。
        Assert.Equal(itemCount, await grain.GetDictionaryCount());
        Assert.Equal(100, await grain.GetListCount());
        Assert.Equal(100, await grain.GetQueueCount());
        Assert.Equal(100, await grain.GetSetCount());

        // 强制停用 Grain。
        var idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 验证（Assert）：检查重新激活后的随机抽取的值。
        for (int i = 0; i < 10; i++)
        {
            var randomIndex = new Random().Next(0, itemCount - 1);
            Assert.Equal(randomIndex, await grain.GetDictionaryValue($"key{randomIndex}"));

            if (randomIndex < 100)
            {
                Assert.Equal($"item{randomIndex}", await grain.GetListItem(randomIndex));
                Assert.True(await grain.ContainsSetItem($"set{randomIndex}"));
            }
        }
    }
}
