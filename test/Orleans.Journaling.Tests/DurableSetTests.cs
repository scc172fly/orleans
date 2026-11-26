using Microsoft.Extensions.Logging;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// DurableSet测试,即使用 Orleans日记的持久性集集执行
/// 用于在 grain 启动和系统重新启动中保持独家收藏的基础设施。
/// 
/// DurableSet 提供标准设定操作(添加、删除、包含),同时确保
/// 元素的独特性和通过日记的持久性。
/// 用于 Orleans grains中独特识别符号、标记或其他不同数值的集合。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestCategory("BVT")]
public class DurableSetTests : JournalingTestBase
{
    /// <summary>
    /// 测试基本操作集:添加、删除和独一性限制。
    /// 证明没有增加重复,而且业务还在持续。
    /// </summary>
    [Fact]
    public async Task DurableSet_BasicOperations_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var set = new DurableSet<string>("testSet", manager, new OrleansBinaryDurableSetCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 法案 - 增列项目
        bool added1 = set.Add("one");
        bool added2 = set.Add("two");
        bool added3 = set.Add("three");
        bool duplicateAdded = set.Add("one"); // 添加重复
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.True(added1);
        Assert.True(added2);
        Assert.True(added3);
        Assert.False(duplicateAdded); // 不应添加重复件
        Assert.Equal(3, set.Count);
        Assert.Equal(["one", "two", "three"], set);

        // 动作 - 删除项目
        bool removed = set.Remove("two");
        bool removedNonExisting = set.Remove("four"); // 删除不存在的
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.True(removed);
        Assert.False(removedNonExisting);
        Assert.Equal(2, set.Count);
        Assert.Equal(["one", "three"], set);
    }

    /// <summary>
    /// 设定状态的测试正确持续并恢复。
    /// 创建一组、添加项目,然后从同一个组重新创建该集
    /// 储存以核查国家恢复情况。
    /// </summary>
    [Fact]
    public async Task DurableSet_Persistence_Test()
    {
        // 第一经理和集
        var sut = CreateTestSystem();
        var codec = CodecProvider.GetCodec<string>();
        var set1 = new DurableSet<string>("testSet", sut.Manager, new OrleansBinaryDurableSetCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // - 增加项目和持续
        set1.Add("one");
        set1.Add("two");
        set1.Add("three");
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        // 创建具有相同存储器的新管理器
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var set2 = new DurableSet<string>("testSet", sut2.Manager, new OrleansBinaryDurableSetCommandCodec<string>(codec, SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 摊销 - 应回收套位
        Assert.Equal(3, set2.Count);
        Assert.Equal(["one", "two", "three"], set2);
    }

    /// <summary>
    /// 测试该集正确处理复杂值类型。
    /// 证明,海关对平等实施适当的目标具有正当效力
    /// 集中保持了独一性。
    /// </summary>
    [Fact]
    public async Task DurableSet_ComplexValues_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<TestPerson>();
        var set = new DurableSet<TestPerson>("personSet", manager, new OrleansBinaryDurableSetCommandCodec<TestPerson>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 法案法
        var person1 = new TestPerson { Id = 1, Name = "John", Age = 30 };
        var person2 = new TestPerson { Id = 2, Name = "Jane", Age = 25 };
        var person3 = new TestPerson { Id = 1, Name = "John", Age = 30 }; // 与人相同1

        set.Add(person1);
        set.Add(person2);
        bool duplicateAdded = set.Add(person3); // 当压倒等值时不应添加重复
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.False(duplicateAdded);
        Assert.Equal(2, set.Count);
        Assert.Equal([person1, person2], set);
    }

    /// <summary>
    /// 测试清除操作,该操作从集中移除所有项目。
    /// 证明清楚操作已正确日记。
    /// </summary>
    [Fact]
    public async Task DurableSet_Clear_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var set = new DurableSet<string>("clearSet", manager, new OrleansBinaryDurableSetCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加项目
        set.Add("one");
        set.Add("two");
        set.Add("three");
        await manager.WriteStateAsync(CancellationToken.None);

        // 动作 - 清除
        set.Clear();
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Empty(set);
    }

    /// <summary>
    /// 测试组查点能力。
    /// 证明这套数据集支持标准查点模式
    /// 并可以转换成其他类型的收藏。
    /// </summary>
    [Fact]
    public async Task DurableSet_Enumeration_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var set = new DurableSet<string>("enumSet", manager, new OrleansBinaryDurableSetCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加项目
        var expectedItems = new HashSet<string> { "one", "two", "three" };

        foreach (var item in expectedItems)
        {
            set.Add(item);
        }

        await manager.WriteStateAsync(CancellationToken.None);

        // 法案法
        var actualItems = set.ToHashSet();

        // 发货
        Assert.Equal(expectedItems, actualItems);
    }

    /// <summary>
    /// 大量物品的设定性能压力测试。
    /// 测试日记系统能够处理数千个独有的物件
    /// 并正确执行独特性限制。
    /// </summary>
    [Fact]
    public async Task DurableSet_LargeNumberOfItems_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<int>();
        var set = new DurableSet<int>("largeSet", manager, new OrleansBinaryDurableSetCommandCodec<int>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 法案 - 增加许多项目
        const int itemCount = 1000;
        for (int i = 0; i < itemCount; i++)
        {
            set.Add(i);
        }

        // 添加一些重复内容, 应予忽略
        for (int i = 0; i < 100; i++)
        {
            set.Add(i);
        }

        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal(itemCount, set.Count);

        // 创建一个具有相同存储器的新管理器, 用于测试回收
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var set2 = new DurableSet<int>("largeSet", sut2.Manager, new OrleansBinaryDurableSetCommandCodec<int>(codec, SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 摊还 - 大集正确回收
        Assert.Equal(itemCount, set2.Count);
        for (int i = 0; i < itemCount; i++)
        {
            Assert.Contains(i, (IReadOnlySet<int>)set2);
        }
    }

    /// <summary>
    /// 使用耐久数据集测试数学集操作。
    /// 演示如何履行交叉、结合和区别
    /// 使用耐久机组的查点能力进行操作。
    /// </summary>
    [Fact]
    public async Task DurableSet_SetOperations_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<int>();
        var set1 = new DurableSet<int>("set1", manager, new OrleansBinaryDurableSetCommandCodec<int>(codec, SessionPool));
        var set2 = new DurableSet<int>("set2", manager, new OrleansBinaryDurableSetCommandCodec<int>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 以偶数从0到10的波号弹出 1 预射
        for (int i = 0; i <= 10; i += 2)
        {
            set1.Add(i);
        }

        // 5-15号的弹射2
        for (int i = 5; i <= 15; i++)
        {
            set2.Add(i);
        }

        await manager.WriteStateAsync(CancellationToken.None);

        // Assert 动作和发售 - 设置操作
        var set1HashSet = set1.ToHashSet();
        var set2HashSet = set2.ToHashSet();

        // 交交
        var intersection = new HashSet<int>(set1HashSet);
        intersection.IntersectWith(set2HashSet);
        Assert.Equal(new HashSet<int> { 6, 8, 10 }, intersection);

        // 工会联盟联盟联盟联盟
        var union = new HashSet<int>(set1HashSet);
        union.UnionWith(set2HashSet);
        Assert.Equal(new HashSet<int> { 0, 2, 4, 6, 8, 10, 5, 7, 9, 11, 12, 13, 14, 15 }, union);

        // 差异(Set1 - set2)
        var difference = new HashSet<int>(set1HashSet);
        difference.ExceptWith(set2HashSet);
        Assert.Equal(new HashSet<int> { 0, 2, 4 }, difference);
    }

    /// <summary>
    /// 测试从一组中选择性地删除多个物品。
    /// 演示如何删除一组项目并进行核查
    /// 其余项目正确保留。
    /// </summary>
    [Fact]
    public async Task DurableSet_ExceptWith_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<int>();
        var set = new DurableSet<int>("exceptSet", manager, new OrleansBinaryDurableSetCommandCodec<int>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加 0 到 9 的数值
        for (int i = 0; i < 10; i++)
        {
            set.Add(i);
        }

        await manager.WriteStateAsync(CancellationToken.None);

        // 动作 - 删除偶数
        var evens = new List<int>();
        for (int i = 0; i < 10; i += 2)
        {
            evens.Add(i);
        }

        foreach (var even in evens)
        {
            set.Remove(even);
        }

        await manager.WriteStateAsync(CancellationToken.None);

        // 发还 - 仅应包含单数
        Assert.Equal(5, set.Count);
        for (int i = 1; i < 10; i += 2)
        {
            Assert.Contains(i, (IReadOnlySet<int>)set);
        }
    }
}
