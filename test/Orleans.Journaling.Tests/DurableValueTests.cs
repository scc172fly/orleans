using Microsoft.Extensions.Logging;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// DurableValue测试,这是一个使用 Orleans日记的简单、持续值的折叠式
/// 基础设施可在 grain 启动和系统重新启动之间保持一个单一值。
/// 
/// DurableValue 是最简单、最持久的数据结构,为持续提供了一种方法
/// 任何类型的单值。 grain 状态属性,需要
/// 收藏的复杂程度不具有持久性。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestCategory("BVT")]
public class DurableValueTests : JournalingTestBase
{
    /// <summary>
    /// 测试基本价值操作:确定和更新价值。
    /// 证明价值变化已正确日记并持续。
    /// </summary>
    [Fact]
    public async Task DurableValue_BasicOperations_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var durableValue = new DurableValue<string>("testValue", manager, new OrleansBinaryDurableValueCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 法案 - 设定初始值
        durableValue.Value = "Hello World";
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal("Hello World", durableValue.Value);

        // AK - 更新价值
        durableValue.Value = "Updated Value";
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal("Updated Value", durableValue.Value);
    }

    /// <summary>
    /// 测试值状态是否正确持续并恢复。
    /// 创建 DurableValue,设置一个值,然后从相同内容中再创建
    /// 储存以核查国家恢复情况。
    /// </summary>
    [Fact]
    public async Task DurableValue_Persistence_Test()
    {
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<int>();
        var durableValue = new DurableValue<int>("counter", manager, new OrleansBinaryDurableValueCommandCodec<int>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 修改和延续
        durableValue.Value = 42;
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        // 创建具有相同存储器的新管理器
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var durableValue2 = new DurableValue<int>("counter", sut2.Manager, new OrleansBinaryDurableValueCommandCodec<int>(codec, SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 摊发 - 应回收价值
        Assert.Equal(42, durableValue2.Value);
    }

    /// <summary>
    /// DurableValue中的空值的测试处理。
    /// 证明无效价值得到恰当坚持,并可以
    /// 与未初始化状态区别开来。
    /// </summary>
    [Fact]
    public async Task DurableValue_NullValue_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string?>();
        var durableValue = new DurableValue<string?>("nullableValue", manager, new OrleansBinaryDurableValueCommandCodec<string?>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 法案 - 设为无效
        durableValue.Value = null;
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Null(durableValue.Value);

        // - 法律 - 更新非无效
        durableValue.Value = "Not null anymore";
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal("Not null anymore", durableValue.Value);

        // Act Act - 更新为无效
        durableValue.Value = null;
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Null(durableValue.Value);
    }

    /// <summary>
    /// 测试 DurableValue 正确处理复杂值类型。
    /// 证明自定制物体已适当按序号编成,
    /// 与存储物体属性的突变仍然存在。
    /// </summary>
    [Fact]
    public async Task DurableValue_ComplexType_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<TestPerson>();
        var durableValue = new DurableValue<TestPerson>("person", manager, new OrleansBinaryDurableValueCommandCodec<TestPerson>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 法案法
        var person = new TestPerson { Id = 1, Name = "John Doe", Age = 30 };
        durableValue.Value = person;
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.NotNull(durableValue.Value);
        Assert.Equal(1, durableValue.Value.Id);
        Assert.Equal("John Doe", durableValue.Value.Name);
        Assert.Equal(30, durableValue.Value.Age);

        // - 更新财产
        durableValue.Value.Age = 31;
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal(31, durableValue.Value.Age);
    }
}
