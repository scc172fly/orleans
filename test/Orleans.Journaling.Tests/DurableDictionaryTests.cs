using Microsoft.Extensions.Logging;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// DurableDictionary测试,这是使用 Orleans日记的持久字典执行测试
/// 基础设施,以维持 grain 启动和系统重新启动的状态。
/// 
/// DurableDictionary 是 Orleans事件采购能力的一部分,提供字典
/// 自动持续所有操作(添加、更新、删除)的
/// 永久日记。 这样可以确保字典状态在失败后能够恢复。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestCategory("BVT")]
public class DurableDictionaryTests : JournalingTestBase
{
    /// <summary>
    /// 测试基本字典操作,包括添加、更新、删除和索引存取。
    /// 证明 DurableDictionary 行为与标准字典一样,而
    /// 通过国家管理人员持续进行国家变革。
    /// </summary>
    [Fact]
    public async Task DurableDictionary_BasicOperations_Test()
    {
        // 排列 - 创建耐久词典,带有字符串键和内值
        // 字典要求日记的序列密钥和值需要编码器
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var keyCodec = CodecProvider.GetCodec<string>();
        var valueCodec = CodecProvider.GetCodec<int>();
        var dictionary = new DurableDictionary<string, int>("testDict", sut.Manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(keyCodec, valueCodec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 动作 - 在字典中添加项目
        // 每个操作都日记, 直至调 WriteStateAsync 时才持续
        dictionary.Add("one", 1);
        dictionary.Add("two", 2);
        dictionary.Add("three", 3);
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal(3, dictionary.Count);
        Assert.Equal(1, dictionary["one"]);
        Assert.Equal(2, dictionary["two"]);
        Assert.Equal(3, dictionary["three"]);

        // 法案 - 更新项目
        dictionary["two"] = 22;
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal(22, dictionary["two"]);

        // 动作 - 删除项目
        var removed = dictionary.Remove("three");
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.True(removed);
        Assert.Equal(2, dictionary.Count);
        Assert.False(dictionary.ContainsKey("three"));
    }

    /// <summary>
    /// 测试词典状态是否正确持续, 并可以恢复 。
    /// 创建字典,添加项目,然后创建一个新的词典实例
    /// 利用同样的储存核查国家恢复情况。
    /// </summary>
    [Fact]
    public async Task DurableDictionary_Persistence_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var keyCodec = CodecProvider.GetCodec<string>();
        var valueCodec = CodecProvider.GetCodec<int>();
        var dictionary1 = new DurableDictionary<string, int>("testDict", sut.Manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(keyCodec, valueCodec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // - 增加项目和持续
        dictionary1.Add("one", 1);
        dictionary1.Add("two", 2);
        dictionary1.Add("three", 3);
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        // 创建一个具有相同存储器的新管理器, 以模拟重新启动后恢复的模拟
        // 测试日记系统重播操作和恢复状态的能力
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var dictionary2 = new DurableDictionary<string, int>("testDict", sut2.Manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(keyCodec, valueCodec, SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 声响 - 应回收词典
        Assert.Equal(3, dictionary2.Count);
        Assert.Equal(1, dictionary2["one"]);
        Assert.Equal(2, dictionary2["two"]);
        Assert.Equal(3, dictionary2["three"]);
    }

    /// <summary>
    /// 测试字典正确处理复杂关键类型。
    /// 证明具有多个属性的自定义键对象适当
    /// 序列、储存和用于调查。
    /// </summary>
    [Fact]
    public async Task DurableDictionary_ComplexKeys_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var keyCodec = CodecProvider.GetCodec<TestKey>();
        var valueCodec = CodecProvider.GetCodec<string>();
        var dictionary = new DurableDictionary<TestKey, string>("complexDict", manager, new OrleansBinaryDurableDictionaryCommandCodec<TestKey, string>(keyCodec, valueCodec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 法案法
        var key1 = new TestKey { Id = 1, Name = "Key1" };
        var key2 = new TestKey { Id = 2, Name = "Key2" };

        dictionary.Add(key1, "Value1");
        dictionary.Add(key2, "Value2");
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal(2, dictionary.Count);
        Assert.Equal("Value1", dictionary[key1]);
        Assert.Equal("Value2", dictionary[key2]);
    }

    /// <summary>
    /// 测试字典正确处理复杂值类型。
    /// 证明具有多个属性的自定义对象可以存储
    /// 并且被回收, 并保持了变异 来检索物体。
    /// </summary>
    [Fact]
    public async Task DurableDictionary_ComplexValues_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var keyCodec = CodecProvider.GetCodec<string>();
        var valueCodec = CodecProvider.GetCodec<TestPerson>();
        var dictionary = new DurableDictionary<string, TestPerson>("peopleDict", manager, new OrleansBinaryDurableDictionaryCommandCodec<string, TestPerson>(keyCodec, valueCodec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 法案法
        var person1 = new TestPerson { Id = 1, Name = "John", Age = 30 };
        var person2 = new TestPerson { Id = 2, Name = "Jane", Age = 25 };

        dictionary.Add("person1", person1);
        dictionary.Add("person2", person2);
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal(2, dictionary.Count);
        Assert.Equal("John", dictionary["person1"].Name);
        Assert.Equal(25, dictionary["person2"].Age);

        // 法案 - 更新
        dictionary["person1"].Age = 31;
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal(31, dictionary["person1"].Age);
    }

    /// <summary>
    /// 测试清除操作,该操作从字典中删除所有项目。
    /// 证明明确操作是适当日记和持续进行的。
    /// </summary>
    [Fact]
    public async Task DurableDictionary_Clear_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var keyCodec = CodecProvider.GetCodec<string>();
        var valueCodec = CodecProvider.GetCodec<int>();
        var dictionary = new DurableDictionary<string, int>("clearDict", manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(keyCodec, valueCodec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加项目
        dictionary.Add("one", 1);
        dictionary.Add("two", 2);
        dictionary.Add("three", 3);
        await manager.WriteStateAsync(CancellationToken.None);

        // 动作 - 清除
        dictionary.Clear();
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Empty(dictionary);
    }

    /// <summary>
    /// 耐久词典的查点能力。
    /// 证明字典支持标准查点模式
    /// 并提供访问钥匙和价值收藏的便利。
    /// </summary>
    [Fact]
    public async Task DurableDictionary_Enumeration_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var keyCodec = CodecProvider.GetCodec<string>();
        var valueCodec = CodecProvider.GetCodec<int>();
        var dictionary = new DurableDictionary<string, int>("enumDict", manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(keyCodec, valueCodec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加项目
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

        // Acout & assert - 测试点查
        var actualPairs = dictionary.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal(expectedPairs, actualPairs);

        // 测试键和价值收藏
        Assert.Equal(expectedPairs.Keys, dictionary.Keys);
        Assert.Equal(expectedPairs.Values, dictionary.Values);
    }
}

/// <summary>
/// 用于验证具有多个属性的复杂密钥的测试键类型
/// 可用于耐久词典。 GenerateSerializer 属性确保
/// Orleans 可以将这种类型序列化, 用于日记 。
/// </summary>
[GenerateSerializer]
public record class TestKey
{
    [Id(0)]
    public int Id { get; set; }
    [Id(1)]
    public string? Name { get; set; }
}
