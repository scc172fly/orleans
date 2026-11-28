using Microsoft.Extensions.Logging;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// DurableQueue 的测试。它是一种持久化 FIFO 队列实现，使用 Orleans 的日志化
/// 基础设施在 Grain 的多次激活及系统重启之间维持状态。
/// 
/// DurableQueue 提供标准队列操作（Enqueue、Dequeue、Peek），同时确保
/// 所有操作均记入日志以保证持久性。这尤其适用于在 Orleans Grain 中实现
/// 可靠的消息处理模式。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestCategory("BVT")]
public class DurableQueueTests : JournalingTestBase
{
    /// <summary>
    /// 测试基本队列操作：Enqueue、Dequeue、Peek 和 Count。
    /// 验证 FIFO 顺序及操作是否正确持久化。
    /// </summary>
    [Fact]
    public async Task DurableQueue_BasicOperations_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var queue = new DurableQueue<string>("testQueue", manager, new OrleansBinaryDurableQueueCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行 - 将元素入队
        queue.Enqueue("one");
        queue.Enqueue("two");
        queue.Enqueue("three");
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal(3, queue.Count);

        // 执行 - 查看队首元素
        var peeked = queue.Peek();

        // 断言 - Peek 返回第一个元素但不将其移除
        Assert.Equal("one", peeked);
        Assert.Equal(3, queue.Count);

        // 执行 - 出队
        var dequeued1 = queue.Dequeue();
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal("one", dequeued1);
        Assert.Equal(2, queue.Count);

        // 执行 - 再次出队
        var dequeued2 = queue.Dequeue();
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal("two", dequeued2);
        Assert.Single(queue);

        // 执行 - 将最后一个元素出队
        var dequeued3 = queue.Dequeue();
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal("three", dequeued3);
        Assert.Empty(queue);
    }

    /// <summary>
    /// 测试队列状态能否正确持久化和恢复。
    /// 创建队列并添加元素，然后使用同一存储重新创建队列，
    /// 以验证状态恢复。
    /// </summary>
    [Fact]
    public async Task DurableQueue_Persistence_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var codec = CodecProvider.GetCodec<string>();
        var queue1 = new DurableQueue<string>("testQueue", sut.Manager, new OrleansBinaryDurableQueueCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行 - 将元素入队并持久化
        queue1.Enqueue("one");
        queue1.Enqueue("two");
        queue1.Enqueue("three");
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        // 使用同一存储创建新的管理器
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var queue2 = new DurableQueue<string>("testQueue", sut2.Manager, new OrleansBinaryDurableQueueCommandCodec<string>(codec, SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 断言 - 队列应已恢复
        Assert.Equal(3, queue2.Count);
        Assert.Equal("one", queue2.Peek());

        // 执行 - 从恢复后的队列中取出元素
        var dequeued = queue2.Dequeue();
        await sut2.Manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal("one", dequeued);
        Assert.Equal(2, queue2.Count);
    }

    /// <summary>
    /// 测试队列能否正确处理复杂对象元素。
    /// 验证自定义对象在入队和出队时能否正确
    /// 序列化和反序列化。
    /// </summary>
    [Fact]
    public async Task DurableQueue_ComplexValues_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<TestPerson>();
        var queue = new DurableQueue<TestPerson>("personQueue", manager, new OrleansBinaryDurableQueueCommandCodec<TestPerson>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行
        var person1 = new TestPerson { Id = 1, Name = "John", Age = 30 };
        var person2 = new TestPerson { Id = 2, Name = "Jane", Age = 25 };

        queue.Enqueue(person1);
        queue.Enqueue(person2);
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal(2, queue.Count);
        var peeked = queue.Peek();
        Assert.Equal("John", peeked.Name);

        // 执行 - 出队
        var dequeued = queue.Dequeue();
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Single(queue);
        Assert.Equal("John", dequeued.Name);
        Assert.Equal(30, dequeued.Age);
    }

    /// <summary>
    /// 测试移除队列中所有元素的 Clear 操作。
    /// 验证清空操作是否正确记入日志。
    /// </summary>
    [Fact]
    public async Task DurableQueue_Clear_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var queue = new DurableQueue<string>("clearQueue", manager, new OrleansBinaryDurableQueueCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加数据项。
        queue.Enqueue("one");
        queue.Enqueue("two");
        queue.Enqueue("three");
        await manager.WriteStateAsync(CancellationToken.None);

        // 执行 - 清空
        queue.Clear();
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Empty(queue);
        Assert.Empty(queue);
    }

    /// <summary>
    /// 测试空队列操作的边界情况。
    /// 验证队列为空时 Peek 和 Dequeue 是否抛出
    /// 适当的异常。
    /// </summary>
    [Fact]
    public async Task DurableQueue_EmptyQueueOperations_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var queue = new DurableQueue<string>("emptyQueue", manager, new OrleansBinaryDurableQueueCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Empty(queue);

        // 执行并断言 - 对空队列调用 Peek 和 Dequeue 应抛出异常
        Assert.Throws<InvalidOperationException>(() => queue.Peek());
        Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
    }

    /// <summary>
    /// 测试队列的枚举能力。
    /// 验证队列是否支持标准枚举模式，
    /// 并按 FIFO 顺序返回元素。
    /// </summary>
    [Fact]
    public async Task DurableQueue_Enumeration_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var queue = new DurableQueue<string>("enumQueue", manager, new OrleansBinaryDurableQueueCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加数据项。
        var expectedItems = new List<string> { "one", "two", "three" };

        foreach (var item in expectedItems)
        {
            queue.Enqueue(item);
        }

        await manager.WriteStateAsync(CancellationToken.None);

        // 执行
        var actualItems = queue.ToList();

        // 断言 - 元素顺序应与入队顺序相同
        Assert.Equal(expectedItems, actualItems);
    }

    /// <summary>
    /// 大量元素下的队列性能压力测试。
    /// 测试日志化系统能否处理数千次队列操作，
    /// 并正确恢复整个队列的状态。
    /// </summary>
    [Fact]
    public async Task DurableQueue_LargeNumberOfOperations_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<int>();
        var queue = new DurableQueue<int>("largeQueue", manager, new OrleansBinaryDurableQueueCommandCodec<int>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行 - 将大量元素入队
        const int itemCount = 1000;
        for (int i = 0; i < itemCount; i++)
        {
            queue.Enqueue(i);
        }

        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal(itemCount, queue.Count);
        Assert.Equal(0, queue.Peek());

        // 使用同一存储创建新的管理器以测试恢复
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var queue2 = new DurableQueue<int>("largeQueue", sut2.Manager, new OrleansBinaryDurableQueueCommandCodec<int>(codec, SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 断言 - 大型队列已正确恢复
        Assert.Equal(itemCount, queue2.Count);

        // 执行 - 将所有元素出队并验证顺序
        for (int i = 0; i < itemCount; i++)
        {
            var item = queue2.Dequeue();
            Assert.Equal(i, item);
        }

        await sut2.Manager.WriteStateAsync(CancellationToken.None);
        Assert.Empty(queue2);
    }

    /// <summary>
    /// 测试入队、出队交错执行的复杂队列操作模式。
    /// 模拟实际使用中分批添加和移除元素的情形，
    /// 验证队列是否维持正确的顺序和状态。
    /// </summary>
    [Fact]
    public async Task DurableQueue_Concurrent_EnqueueDequeue_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<int>();
        var queue = new DurableQueue<int>("concurrentQueue", manager, new OrleansBinaryDurableQueueCommandCodec<int>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行 - 模拟队列上的并发操作
        const int batchSize = 100;

        // 第一批：添加 100 个元素
        for (int i = 0; i < batchSize; i++)
        {
            queue.Enqueue(i);
        }
        await manager.WriteStateAsync(CancellationToken.None);

        // 移除 50 个元素
        for (int i = 0; i < batchSize / 2; i++)
        {
            queue.Dequeue();
        }
        await manager.WriteStateAsync(CancellationToken.None);

        // 再添加 100 个元素
        for (int i = batchSize; i < batchSize * 2; i++)
        {
            queue.Enqueue(i);
        }
        await manager.WriteStateAsync(CancellationToken.None);

        // 验证
        Assert.Equal(batchSize + batchSize / 2, queue.Count); // 应有 150 个元素

        // 使用同一存储创建新的管理器以测试恢复
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var queue2 = new DurableQueue<int>("concurrentQueue", sut2.Manager, new OrleansBinaryDurableQueueCommandCodec<int>(codec, SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 断言 - 队列应以正确的状态和顺序恢复
        Assert.Equal(batchSize + batchSize / 2, queue2.Count);

        // 最先得到的值应为第一批的后半部分
        for (int i = batchSize / 2; i < batchSize; i++)
        {
            var item = queue2.Dequeue();
            Assert.Equal(i, item);
        }

        // 然后应得到第二批元素
        for (int i = batchSize; i < batchSize * 2; i++)
        {
            var item = queue2.Dequeue();
            Assert.Equal(i, item);
        }

        await sut2.Manager.WriteStateAsync(CancellationToken.None);
        Assert.Empty(queue2);
    }
}
