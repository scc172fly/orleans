using Microsoft.Extensions.Logging;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// DurableSet 的测试。它是一种持久化集合实现，使用 Orleans 的日志化
/// 基础设施在 Grain 的多次激活及系统重启之间维持元素唯一的集合。
/// 
/// DurableSet 提供标准集合操作（Add、Remove、Contains），同时确保
/// 元素的唯一性，并通过日志化保证持久性。这适用于在 Orleans Grain 中维护
/// 唯一标识符、标签或其他互不相同的值的集合。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestCategory("BVT")]
public class DurableSetTests : JournalingTestBase
{
    /// <summary>
    /// 测试基本集合操作：Add、Remove 及唯一性约束。
    /// 验证重复元素不会被添加，且操作会被持久化。
    /// </summary>
    [Fact]
    public async Task DurableSet_BasicOperations_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var set = new DurableSet<string>("testSet", manager, new OrleansBinaryDurableSetCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行 - 添加元素
        bool added1 = set.Add("one");
        bool added2 = set.Add("two");
        bool added3 = set.Add("three");
        bool duplicateAdded = set.Add("one"); // 添加重复元素
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.True(added1);
        Assert.True(added2);
        Assert.True(added3);
        Assert.False(duplicateAdded); // 不应添加重复元素
        Assert.Equal(3, set.Count);
        Assert.Equal(["one", "two", "three"], set);

        // 执行 - 移除元素
        bool removed = set.Remove("two");
        bool removedNonExisting = set.Remove("four"); // 移除不存在的元素
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.True(removed);
        Assert.False(removedNonExisting);
        Assert.Equal(2, set.Count);
        Assert.Equal(["one", "three"], set);
    }

    /// <summary>
    /// 测试集合状态能否正确持久化和恢复。
    /// 创建集合并添加元素，然后使用同一存储重新创建集合，
    /// 以验证状态恢复。
    /// </summary>
    [Fact]
    public async Task DurableSet_Persistence_Test()
    {
        // 第一个管理器及集合
        var sut = CreateTestSystem();
        var codec = CodecProvider.GetCodec<string>();
        var set1 = new DurableSet<string>("testSet", sut.Manager, new OrleansBinaryDurableSetCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行 - 添加元素并持久化
        set1.Add("one");
        set1.Add("two");
        set1.Add("three");
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        // 使用同一存储创建新的管理器
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var set2 = new DurableSet<string>("testSet", sut2.Manager, new OrleansBinaryDurableSetCommandCodec<string>(codec, SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 断言 - 集合应已恢复
        Assert.Equal(3, set2.Count);
        Assert.Equal(["one", "two", "three"], set2);
    }

    /// <summary>
    /// 测试集合能否正确处理复杂对象元素。
    /// 验证正确实现相等性比较的自定义对象
    /// 在集合中能否保持唯一性。
    /// </summary>
    [Fact]
    public async Task DurableSet_ComplexValues_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<TestPerson>();
        var set = new DurableSet<TestPerson>("personSet", manager, new OrleansBinaryDurableSetCommandCodec<TestPerson>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行
        var person1 = new TestPerson { Id = 1, Name = "John", Age = 30 };
        var person2 = new TestPerson { Id = 2, Name = "Jane", Age = 25 };
        var person3 = new TestPerson { Id = 1, Name = "John", Age = 30 }; // 与 person1 相同

        set.Add(person1);
        set.Add(person2);
        bool duplicateAdded = set.Add(person3); // 重写 Equals 后不应添加重复元素
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.False(duplicateAdded);
        Assert.Equal(2, set.Count);
        Assert.Equal([person1, person2], set);
    }

    /// <summary>
    /// 测试移除集合中所有元素的 Clear 操作。
    /// 验证清空操作是否正确记入日志。
    /// </summary>
    [Fact]
    public async Task DurableSet_Clear_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var set = new DurableSet<string>("clearSet", manager, new OrleansBinaryDurableSetCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加数据项。
        set.Add("one");
        set.Add("two");
        set.Add("three");
        await manager.WriteStateAsync(CancellationToken.None);

        // 执行 - 清空
        set.Clear();
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Empty(set);
    }

    /// <summary>
    /// 测试集合的枚举能力。
    /// 验证集合是否支持标准枚举模式，
    /// 且能够转换为其他集合类型。
    /// </summary>
    [Fact]
    public async Task DurableSet_Enumeration_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var set = new DurableSet<string>("enumSet", manager, new OrleansBinaryDurableSetCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加数据项。
        var expectedItems = new HashSet<string> { "one", "two", "three" };

        foreach (var item in expectedItems)
        {
            set.Add(item);
        }

        await manager.WriteStateAsync(CancellationToken.None);

        // 执行
        var actualItems = set.ToHashSet();

        // 验证
        Assert.Equal(expectedItems, actualItems);
    }

    /// <summary>
    /// 大量元素下的集合性能压力测试。
    /// 测试日志化系统能否处理数千个唯一元素，
    /// 并正确实施唯一性约束。
    /// </summary>
    [Fact]
    public async Task DurableSet_LargeNumberOfItems_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<int>();
        var set = new DurableSet<int>("largeSet", manager, new OrleansBinaryDurableSetCommandCodec<int>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行 - 添加大量元素
        const int itemCount = 1000;
        for (int i = 0; i < itemCount; i++)
        {
            set.Add(i);
        }

        // 添加一些应被忽略的重复元素
        for (int i = 0; i < 100; i++)
        {
            set.Add(i);
        }

        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal(itemCount, set.Count);

        // 使用同一存储创建新的管理器以测试恢复
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var set2 = new DurableSet<int>("largeSet", sut2.Manager, new OrleansBinaryDurableSetCommandCodec<int>(codec, SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 断言 - 大型集合已正确恢复
        Assert.Equal(itemCount, set2.Count);
        for (int i = 0; i < itemCount; i++)
        {
            Assert.Contains(i, (IReadOnlySet<int>)set2);
        }
    }

    /// <summary>
    /// 测试使用持久化集合进行数学集合运算。
    /// 演示如何利用持久化集合的枚举能力执行交集、并集
    /// 和差集运算。
    /// </summary>
    [Fact]
    public async Task DurableSet_SetOperations_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<int>();
        var set1 = new DurableSet<int>("set1", manager, new OrleansBinaryDurableSetCommandCodec<int>(codec, SessionPool));
        var set2 = new DurableSet<int>("set2", manager, new OrleansBinaryDurableSetCommandCodec<int>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 使用 0 到 10 之间的偶数填充 set1
        for (int i = 0; i <= 10; i += 2)
        {
            set1.Add(i);
        }

        // 使用 5 到 15 之间的数字填充 set2
        for (int i = 5; i <= 15; i++)
        {
            set2.Add(i);
        }

        await manager.WriteStateAsync(CancellationToken.None);

        // 执行并断言 - 集合运算
        var set1HashSet = set1.ToHashSet();
        var set2HashSet = set2.ToHashSet();

        // 交集
        var intersection = new HashSet<int>(set1HashSet);
        intersection.IntersectWith(set2HashSet);
        Assert.Equal(new HashSet<int> { 6, 8, 10 }, intersection);

        // 并集
        var union = new HashSet<int>(set1HashSet);
        union.UnionWith(set2HashSet);
        Assert.Equal(new HashSet<int> { 0, 2, 4, 6, 8, 10, 5, 7, 9, 11, 12, 13, 14, 15 }, union);

        // 差集（set1 - set2）
        var difference = new HashSet<int>(set1HashSet);
        difference.ExceptWith(set2HashSet);
        Assert.Equal(new HashSet<int> { 0, 2, 4 }, difference);
    }

    /// <summary>
    /// 测试有选择地从集合中移除多个元素。
    /// 演示如何移除一部分元素，并验证
    /// 其余元素是否正确持久化。
    /// </summary>
    [Fact]
    public async Task DurableSet_ExceptWith_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<int>();
        var set = new DurableSet<int>("exceptSet", manager, new OrleansBinaryDurableSetCommandCodec<int>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加 0 到 9 的数字
        for (int i = 0; i < 10; i++)
        {
            set.Add(i);
        }

        await manager.WriteStateAsync(CancellationToken.None);

        // 执行 - 移除偶数
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

        // 断言 - 应仅包含奇数
        Assert.Equal(5, set.Count);
        for (int i = 1; i < 10; i += 2)
        {
            Assert.Contains(i, (IReadOnlySet<int>)set);
        }
    }
}
