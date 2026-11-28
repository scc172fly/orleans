using Microsoft.Extensions.Logging;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// DurableDictionary 的测试。它是一个持久化字典实现，利用 Orleans 的日志化
/// 基础设施跨 Grain 激活实例和系统重启保留状态。
/// 
/// DurableDictionary 是 Orleans 事件溯源能力的一部分，提供一种字典
/// 数据结构，可将所有操作（添加、更新、删除）自动持久化到
/// 持久化日志，从而确保发生故障后可以恢复字典状态。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestCategory("BVT")]
public class DurableDictionaryTests : JournalingTestBase
{
    /// <summary>
    /// 测试字典的基本操作，包括添加、更新、删除及索引器访问。
    /// 验证 DurableDictionary 在表现得与标准字典一致的同时，
    /// 通过状态管理器持久化状态变更。
    /// </summary>
    [Fact]
    public async Task DurableDictionary_BasicOperations_Test()
    {
        // 准备（Arrange）：创建键为 string、值为 int 的持久化字典。
        // 字典需要编解码器，以便将键和值序列化到日志中。
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var keyCodec = CodecProvider.GetCodec<string>();
        var valueCodec = CodecProvider.GetCodec<int>();
        var dictionary = new DurableDictionary<string, int>("testDict", sut.Manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(keyCodec, valueCodec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行（Act）：向字典添加条目。
        // 每个操作都会记入日志，但直到调用 WriteStateAsync 才会持久化。
        dictionary.Add("one", 1);
        dictionary.Add("two", 2);
        dictionary.Add("three", 3);
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal(3, dictionary.Count);
        Assert.Equal(1, dictionary["one"]);
        Assert.Equal(2, dictionary["two"]);
        Assert.Equal(3, dictionary["three"]);

        // 执行（Act）：更新条目。
        dictionary["two"] = 22;
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal(22, dictionary["two"]);

        // 执行（Act）：删除条目。
        var removed = dictionary.Remove("three");
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.True(removed);
        Assert.Equal(2, dictionary.Count);
        Assert.False(dictionary.ContainsKey("three"));
    }

    /// <summary>
    /// 测试字典状态是否正确持久化并能够恢复。
    /// 创建字典并添加条目，然后使用相同的存储
    /// 创建新的字典实例来验证状态恢复。
    /// </summary>
    [Fact]
    public async Task DurableDictionary_Persistence_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var keyCodec = CodecProvider.GetCodec<string>();
        var valueCodec = CodecProvider.GetCodec<int>();
        var dictionary1 = new DurableDictionary<string, int>("testDict", sut.Manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(keyCodec, valueCodec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行（Act）：添加条目并持久化。
        dictionary1.Add("one", 1);
        dictionary1.Add("two", 2);
        dictionary1.Add("three", 3);
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        // 使用相同的存储创建新的管理器，以模拟重启后的恢复过程。
        // 这会测试日志化系统重放操作并恢复状态的能力。
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var dictionary2 = new DurableDictionary<string, int>("testDict", sut2.Manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(keyCodec, valueCodec, SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 验证（Assert）：应恢复字典。
        Assert.Equal(3, dictionary2.Count);
        Assert.Equal(1, dictionary2["one"]);
        Assert.Equal(2, dictionary2["two"]);
        Assert.Equal(3, dictionary2["three"]);
    }

    /// <summary>
    /// 测试字典能否正确处理复杂的键类型。
    /// 验证具有多个属性的自定义键对象可以正确
    /// 序列化、存储并用于查找。
    /// </summary>
    [Fact]
    public async Task DurableDictionary_ComplexKeys_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var keyCodec = CodecProvider.GetCodec<TestKey>();
        var valueCodec = CodecProvider.GetCodec<string>();
        var dictionary = new DurableDictionary<TestKey, string>("complexDict", manager, new OrleansBinaryDurableDictionaryCommandCodec<TestKey, string>(keyCodec, valueCodec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行
        var key1 = new TestKey { Id = 1, Name = "Key1" };
        var key2 = new TestKey { Id = 2, Name = "Key2" };

        dictionary.Add(key1, "Value1");
        dictionary.Add(key2, "Value2");
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal(2, dictionary.Count);
        Assert.Equal("Value1", dictionary[key1]);
        Assert.Equal("Value2", dictionary[key2]);
    }

    /// <summary>
    /// 测试字典能否正确处理用作值的复杂对象。
    /// 验证具有多个属性的自定义对象可以存储
    /// 和检索，并且对检索出的对象所作的变更可以持久化。
    /// </summary>
    [Fact]
    public async Task DurableDictionary_ComplexValues_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var keyCodec = CodecProvider.GetCodec<string>();
        var valueCodec = CodecProvider.GetCodec<TestPerson>();
        var dictionary = new DurableDictionary<string, TestPerson>("peopleDict", manager, new OrleansBinaryDurableDictionaryCommandCodec<string, TestPerson>(keyCodec, valueCodec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行
        var person1 = new TestPerson { Id = 1, Name = "John", Age = 30 };
        var person2 = new TestPerson { Id = 2, Name = "Jane", Age = 25 };

        dictionary.Add("person1", person1);
        dictionary.Add("person2", person2);
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal(2, dictionary.Count);
        Assert.Equal("John", dictionary["person1"].Name);
        Assert.Equal(25, dictionary["person2"].Age);

        // 执行（Act）：更新。
        dictionary["person1"].Age = 31;
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal(31, dictionary["person1"].Age);
    }

    /// <summary>
    /// 测试移除字典中所有条目的 Clear 操作。
    /// 验证清空操作能够正确记入日志并持久化。
    /// </summary>
    [Fact]
    public async Task DurableDictionary_Clear_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var keyCodec = CodecProvider.GetCodec<string>();
        var valueCodec = CodecProvider.GetCodec<int>();
        var dictionary = new DurableDictionary<string, int>("clearDict", manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(keyCodec, valueCodec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加数据项。
        dictionary.Add("one", 1);
        dictionary.Add("two", 2);
        dictionary.Add("three", 3);
        await manager.WriteStateAsync(CancellationToken.None);

        // 执行（Act）：清空字典。
        dictionary.Clear();
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Empty(dictionary);
    }

    /// <summary>
    /// 测试持久化字典的枚举能力。
    /// 验证字典支持标准枚举模式，
    /// 并提供对 Keys 和 Values 集合的访问。
    /// </summary>
    [Fact]
    public async Task DurableDictionary_Enumeration_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var keyCodec = CodecProvider.GetCodec<string>();
        var valueCodec = CodecProvider.GetCodec<int>();
        var dictionary = new DurableDictionary<string, int>("enumDict", manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(keyCodec, valueCodec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加数据项。
        var expectedPairs = new Dictionary<string, int>
        {
            { "one", 1 },
            { "two", 2 },
            { "three", 3 }
        };

        foreach (var pair in expectedPairs)
        {
            dictionary.Add(pair.Key, pair.Value);
        }

        await manager.WriteStateAsync(CancellationToken.None);

        // 执行并验证（Act & Assert）：测试枚举。
        var actualPairs = dictionary.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal(expectedPairs, actualPairs);

        // 测试 Keys 和 Values 集合。
        Assert.Equal(expectedPairs.Keys, dictionary.Keys);
        Assert.Equal(expectedPairs.Values, dictionary.Values);
    }
}

/// <summary>
/// 用于验证具有多个属性的复杂键可以在持久化字典中
/// 使用的测试键类型。GenerateSerializer 特性确保
/// Orleans 能够为日志化处理序列化该类型。
/// </summary>
[GenerateSerializer]
public record class TestKey
{
    [Id(0)]
    public int Id { get; set; }
    [Id(1)]
    public string? Name { get; set; }
}
