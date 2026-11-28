using Microsoft.Extensions.Logging;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// DurableValue 的测试。它是一种简单的持久化值包装器，使用 Orleans 的日志化
/// 基础设施在 Grain 的多次激活及系统重启之间维持单个值。
/// 
/// DurableValue 是最简单的持久化数据结构，可用于持久化
/// 任意类型的单个值。它适用于需要持久性而
/// 无需引入复杂集合结构的 Grain 状态属性。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestCategory("BVT")]
public class DurableValueTests : JournalingTestBase
{
    /// <summary>
    /// 测试基本值操作：设置和更新值。
    /// 验证值的变更是否正确记入日志并持久化。
    /// </summary>
    [Fact]
    public async Task DurableValue_BasicOperations_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var durableValue = new DurableValue<string>("testValue", manager, new OrleansBinaryDurableValueCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行 - 设置初始值
        durableValue.Value = "Hello World";
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal("Hello World", durableValue.Value);

        // 执行 - 更新值
        durableValue.Value = "Updated Value";
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal("Updated Value", durableValue.Value);
    }

    /// <summary>
    /// 测试值的状态能否正确持久化和恢复。
    /// 创建 DurableValue 并设置一个值，然后使用同一存储重新创建它，
    /// 以验证状态恢复。
    /// </summary>
    [Fact]
    public async Task DurableValue_Persistence_Test()
    {
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<int>();
        var durableValue = new DurableValue<int>("counter", manager, new OrleansBinaryDurableValueCommandCodec<int>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行 - 修改并持久化
        durableValue.Value = 42;
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        // 使用同一存储创建新的管理器
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var durableValue2 = new DurableValue<int>("counter", sut2.Manager, new OrleansBinaryDurableValueCommandCodec<int>(codec, SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 断言 - 值应已恢复
        Assert.Equal(42, durableValue2.Value);
    }

    /// <summary>
    /// 测试 DurableValue 对 null 值的处理。
    /// 验证 null 值是否正确持久化，以及能否
    /// 与未初始化状态区分开来。
    /// </summary>
    [Fact]
    public async Task DurableValue_NullValue_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string?>();
        var durableValue = new DurableValue<string?>("nullableValue", manager, new OrleansBinaryDurableValueCommandCodec<string?>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行 - 设置为 null
        durableValue.Value = null;
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Null(durableValue.Value);

        // 执行 - 更新为非 null 值
        durableValue.Value = "Not null anymore";
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal("Not null anymore", durableValue.Value);

        // 执行 - 更新回 null
        durableValue.Value = null;
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Null(durableValue.Value);
    }

    /// <summary>
    /// 测试 DurableValue 能否正确处理复杂对象值。
    /// 验证自定义对象是否正确序列化，以及
    /// 对已存储对象属性的修改是否被持久化。
    /// </summary>
    [Fact]
    public async Task DurableValue_ComplexType_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<TestPerson>();
        var durableValue = new DurableValue<TestPerson>("person", manager, new OrleansBinaryDurableValueCommandCodec<TestPerson>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行
        var person = new TestPerson { Id = 1, Name = "John Doe", Age = 30 };
        durableValue.Value = person;
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.NotNull(durableValue.Value);
        Assert.Equal(1, durableValue.Value.Id);
        Assert.Equal("John Doe", durableValue.Value.Name);
        Assert.Equal(30, durableValue.Value.Age);

        // 执行 - 更新属性
        durableValue.Value.Age = 31;
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal(31, durableValue.Value.Age);
    }
}
