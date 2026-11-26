using Orleans.Core.Internal;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 综合测试 DurableGrain 功能在 Orleans.
/// 
/// DurableGrain 是一个基级,使用 Orleans的日记提供自动状态持久性
/// 它将确保 grain 状态能够生存下来,使 grain 的停用、节点故障和
/// 系统重新启动, 将所有状态修改都编辑到耐用日记上 。
/// 
/// 这些测试核实:
/// - 国家持久性 grain 激活
/// - 支助复杂类型和收藏
/// - 单一个 grain多耐久收藏
/// - 大型国家处理
/// </summary>
[TestCategory("BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Journaling")]
public class DurableGrainTests(IntegrationTestFixture fixture) : IClassFixture<IntegrationTestFixture>
{
    private IGrainFactory Client => fixture.Client;

    /// <summary>
    /// 测试耐久性 grain的基本状态持久性。
    /// 证明简单状态属性(字符串和英特)正确无误
    /// grain 停止使用后,一直存在并恢复。
    /// </summary>
    [Fact]
    public async Task DurableGrain_State_Persistence_Test()
    {
        // 排列排列
        var grain = Client.GetGrain<ITestDurableGrain>(Guid.NewGuid());

        // - 确定国有财产和永久财产的法律
        await grain.SetTestValues("Test Name", 42);

        // 发货
        Assert.Equal("Test Name", await grain.GetName());
        Assert.Equal(42, await grain.GetCounter());

        // 强制停止功能并获取新的引用
        // 此模拟 grain 失败或节点重新启动假想
        var idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 摊还 - 应当收回国家
        Assert.Equal("Test Name", await grain.GetName());
        Assert.Equal(42, await grain.GetCounter());
    }

    /// <summary>
    /// 测试状态更新是否正确日记并持续进行。
    /// 证明最新的状态值在停止使用后恢复,
    /// 而不是初始值。
    /// </summary>
    [Fact]
    public async Task DurableGrain_Update_State_Test()
    {
        // 排列排列
        var grain = Client.GetGrain<ITestDurableGrain>(Guid.NewGuid());

        // - 确定并维持
        await grain.SetTestValues("Initial Name", 10);

        // 更新状态并再次持续
        await grain.SetTestValues("Updated Name", 20);

        // 发货
        Assert.Equal("Updated Name", await grain.GetName());
        Assert.Equal(20, await grain.GetCounter());

        // 强制停止功能并获取新的引用
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);

        // 摊还 - 应恢复更新状态
        Assert.Equal("Updated Name", await grain.GetName());
        Assert.Equal(20, await grain.GetCounter());
    }

    /// <summary>
    /// 测试复杂种类的持久性,包括定制物品和收藏品。
    /// 证明 Orleans的序列序列正确处理嵌套对象
    /// 和所收集的, grain 状态。
    /// </summary>
    [Fact]
    public async Task DurableGrain_Complex_Types_Test()
    {
        // 排列排列
        var grain = Client.GetGrain<ITestDurableGrainWithComplexState>(Guid.NewGuid());

        // - 确定复杂状态并维持
        var person = new TestPerson { Id = 1, Name = "John Doe", Age = 30 };
        var items = new List<string> { "Item1", "Item2", "Item3" };
        await grain.SetTestValues(person, items);

        // 发货
        var retrievedPerson = await grain.GetPerson();
        var retrievedItems = await grain.GetItems();

        Assert.Equal("John Doe", retrievedPerson.Name);
        Assert.Equal(3, retrievedItems.Count);

        // 强制停止功能并获取新的引用
        // 此模拟 grain 失败或节点重新启动假想
        var idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 摊还 - 应恢复复杂状态
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
    /// 测试同时使用多个耐久收藏的 grain 。
    /// 证明 DurableDictionary、 DurableList、 DurableQueue和 DurableSet
    /// 在一个 grain 内可以共存,独立地维持状态。
    /// </summary>
    [Fact]
    public async Task DurableGrain_Multiple_Collections_Test()
    {
        // 排列排列
        var grain = Client.GetGrain<ITestMultiCollectionGrain>(Guid.NewGuid());

        // - 法书刊集并持续
        await grain.AddToDictionary("key1", 1);
        await grain.AddToDictionary("key2", 2);
        await grain.AddToList("item1");
        await grain.AddToList("item2");
        await grain.AddToQueue(100);
        await grain.AddToQueue(200);
        await grain.AddToSet("set1");
        await grain.AddToSet("set2");

        // 发货
        Assert.Equal(2, await grain.GetDictionaryCount());
        Assert.Equal(2, await grain.GetListCount());
        Assert.Equal(2, await grain.GetQueueCount());
        Assert.Equal(2, await grain.GetSetCount());

        // 强制停止功能并获取新的引用
        // 此模拟 grain 失败或节点重新启动假想
        var idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 摊还 - 所有收藏应全部收回
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
    /// 测试复杂的国家修改设想方案,包括更新和清除。
    /// 证明所有修改操作(添加、更新、删除)均适当
    /// 日记和最后状态恢复正确 。
    /// </summary>
    [Fact]
    public async Task DurableGrain_State_Modifications_Test()
    {
        // 排列排列
        var grain = Client.GetGrain<ITestMultiCollectionGrain>(Guid.NewGuid());

        // - 法案 -- -- 最初的状态和持续
        await grain.AddToDictionary("key1", 1);
        await grain.AddToList("item1");
        await grain.AddToQueue(100);
        await grain.AddToSet("set1");

        // 修改状态并再次持续
        await grain.AddToDictionary("key2", 2);
        await grain.AddToDictionary("key1", 10); // 通过界面方法更新
        await grain.AddToList("item2");
        await grain.AddToQueue(200);
        await grain.AddToSet("set2");

        // 发货
        Assert.Equal(2, await grain.GetDictionaryCount());
        Assert.Equal(10, await grain.GetDictionaryValue("key1"));
        Assert.Equal(2, await grain.GetListCount());
        Assert.Equal(2, await grain.GetQueueCount());
        Assert.Equal(2, await grain.GetSetCount());

        // 强制停止功能并获取新的引用
        // 此模拟 grain 失败或节点重新启动假想
        var idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 安全状态 - 应恢复修改状态
        Assert.Equal(2, await grain.GetDictionaryCount());
        Assert.Equal(10, await grain.GetDictionaryValue("key1"));
        Assert.Equal(2, await grain.GetDictionaryValue("key2"));

        Assert.Equal(2, await grain.GetListCount());
        Assert.Equal("item1", await grain.GetListItem(0));
        Assert.Equal("item2", await grain.GetListItem(1));

        // 进一步修改状态
        await grain.RemoveFromDictionary("key1");
        await grain.RemoveListItemAt(0);
        await grain.DequeueItem();
        await grain.RemoveFromSet("set1");

        // 宣布修改
        Assert.Equal(1, await grain.GetDictionaryCount());
        Assert.Equal(1, await grain.GetListCount());
        Assert.Equal(1, await grain.GetQueueCount());
        Assert.Equal(1, await grain.GetSetCount());
    }

    /// <summary>
    /// 测试 grain 使用不同 grain 接口。
    /// 这一测试为耐久机制提供了额外的核查
    /// 使用简单的 grain 接口。
    /// </summary>
    [Fact]
    public async Task Grain_State_Should_Persist_Between_Activations()
    {
        // 排列 - 获取 grain引用
        var grain = Client.GetGrain<ITestDurableGrainInterface>(Guid.NewGuid());

        // 法律 - 确定 grain 状态
        await grain.SetValues("Test Name", 42);
        var initialState = await grain.GetValues();

        // 强行解除 grain
        var idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 从 grain (将恢复) 获取值
        var newState = await grain.GetValues();

        // 发货
        Assert.Equal(initialState.Name, newState.Name);
        Assert.Equal(initialState.Counter, newState.Counter);
    }

    /// <summary>
    /// 全面测试多重收集作业和持久性。
    /// 对所有收藏类型添加、删除和查询操作
    /// 核实多个解除作用周期的状态一致性。
    /// </summary>
    [Fact]
    public async Task Grain_Should_Handle_Multiple_Collections()
    {
        // 排列排列
        var grain = Client.GetGrain<ITestMultiCollectionGrain>(Guid.NewGuid());

        // 动作 - 在收藏中添加项目
        await grain.AddToDictionary("key1", 1);
        await grain.AddToDictionary("key2", 2);

        await grain.AddToList("item1");
        await grain.AddToList("item2");

        await grain.AddToQueue(100);
        await grain.AddToQueue(200);

        await grain.AddToSet("set1");
        await grain.AddToSet("set2");
        await grain.AddToSet("set1"); // 重复, 应该忽略

        // 发还 - 检查计数
        Assert.Equal(2, await grain.GetDictionaryCount());
        Assert.Equal(2, await grain.GetListCount());
        Assert.Equal(2, await grain.GetQueueCount());
        Assert.Equal(2, await grain.GetSetCount());

        // 强行解除 grain
        var idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 摊销 - 恢复激活后检查值
        Assert.Equal(1, await grain.GetDictionaryValue("key1"));
        Assert.Equal(2, await grain.GetDictionaryValue("key2"));
        Assert.Equal("item1", await grain.GetListItem(0));
        Assert.Equal("item2", await grain.GetListItem(1));
        Assert.Equal(100, await grain.PeekQueueItem());
        Assert.True(await grain.ContainsSetItem("set1"));
        Assert.True(await grain.ContainsSetItem("set2"));

        // 法案 - 修改收藏
        await grain.RemoveFromDictionary("key1");
        await grain.RemoveListItemAt(0);
        await grain.DequeueItem();
        await grain.RemoveFromSet("set1");

        // 摊销 - 经修改后检查计数
        Assert.Equal(1, await grain.GetDictionaryCount());
        Assert.Equal(1, await grain.GetListCount());
        Assert.Equal(1, await grain.GetQueueCount());
        Assert.Equal(1, await grain.GetSetCount());

        // 再次取消 grain
        idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 发还 - 第二次重新启动后检查值
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
    /// 耐久 grains大规模国家处理压力测试。
    /// 证明日记系统可以用千位处理 grains
    /// 国家条目中不出现性能退化或数据损失。
    /// </summary>
    [Fact]
    public async Task Grain_Should_Handle_Large_State()
    {
        // 排列排列
        var grain = Client.GetGrain<ITestMultiCollectionGrain>(Guid.NewGuid());

        // 法案 - 增加许多项目
        const int itemCount = 1000;
        for (int i = 0; i < itemCount; i++)
        {
            await grain.AddToDictionary($"key{i}", i);
            if (i < 100) // 将较少的项目添加到其他收藏项中,以保持测试运行时间合理
            {
                await grain.AddToList($"item{i}");
                await grain.AddToQueue(i);
                await grain.AddToSet($"set{i}");
            }
        }

        // 发还 - 检查计数
        Assert.Equal(itemCount, await grain.GetDictionaryCount());
        Assert.Equal(100, await grain.GetListCount());
        Assert.Equal(100, await grain.GetQueueCount());
        Assert.Equal(100, await grain.GetSetCount());

        // 强行解除 grain
        var idBefore = await grain.GetActivationId();
        await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);
        Assert.NotEqual(idBefore, await grain.GetActivationId());

        // 发还 - 在恢复激活后检查随机值
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
