using Microsoft.Extensions.Logging;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// DurableQueue测试,这是使用 Orleans日记的FIFO 长期性队列执行测试
/// 基础设施,以维持 grain 启动和系统重新启动的状态。
/// 
/// DurableQueue 提供标准队列操作(Enqueue、dequeue、peek),同时确保
/// 所有操作都为耐久性日记。这对于执行特别有用
/// Orleans grains中的可靠信件处理模式。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestCategory("BVT")]
public class DurableQueueTests : JournalingTestBase
{
    /// <summary>
    /// 测试基本队列操作: Enqueue、 dequeue、 Peek 和 Count。
    /// 核证FIFO订购,操作正确持续。
    /// </summary>
    [Fact]
    public async Task DurableQueue_BasicOperations_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var queue = new DurableQueue<string>("testQueue", manager, new OrleansBinaryDurableQueueCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 动作 - 内容
        queue.Enqueue("one");
        queue.Enqueue("two");
        queue.Enqueue("three");
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal(3, queue.Count);

        // · 排队前的《花花一角》
        var peeked = queue.Peek();

        // Assert - Peek 返回第一个项目,但没有删除它
        Assert.Equal("one", peeked);
        Assert.Equal(3, queue.Count);

        // 法案 - 删除
        var dequeued1 = queue.Dequeue();
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal("one", dequeued1);
        Assert.Equal(2, queue.Count);

        // 法案 - 再次取消
        var dequeued2 = queue.Dequeue();
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal("two", dequeued2);
        Assert.Single(queue);

        // 动作 - 删除最后一个项目
        var dequeued3 = queue.Dequeue();
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal("three", dequeued3);
        Assert.Empty(queue);
    }

    /// <summary>
    /// 队列状态正确持续并恢复的测试。
    /// 创建队列、添加项目,然后从相同内容重建队列
    /// 储存以核查国家恢复情况。
    /// </summary>
    [Fact]
    public async Task DurableQueue_Persistence_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var codec = CodecProvider.GetCodec<string>();
        var queue1 = new DurableQueue<string>("testQueue", sut.Manager, new OrleansBinaryDurableQueueCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // - 持续实施和保留的项目
        queue1.Enqueue("one");
        queue1.Enqueue("two");
        queue1.Enqueue("three");
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        // 创建具有相同存储器的新管理器
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var queue2 = new DurableQueue<string>("testQueue", sut2.Manager, new OrleansBinaryDurableQueueCommandCodec<string>(codec, SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 摊储 - 应回收队列
        Assert.Equal(3, queue2.Count);
        Assert.Equal("one", queue2.Peek());

        // - 法案 - 从已恢复的队列中解排
        var dequeued = queue2.Dequeue();
        await sut2.Manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal("one", dequeued);
        Assert.Equal(2, queue2.Count);
    }

    /// <summary>
    /// 测试队列正确处理复杂值类型。
    /// 证明自定制物体已适当序列化和消散
    /// 并且记念它,
    /// </summary>
    [Fact]
    public async Task DurableQueue_ComplexValues_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<TestPerson>();
        var queue = new DurableQueue<TestPerson>("personQueue", manager, new OrleansBinaryDurableQueueCommandCodec<TestPerson>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 法案法
        var person1 = new TestPerson { Id = 1, Name = "John", Age = 30 };
        var person2 = new TestPerson { Id = 2, Name = "Jane", Age = 25 };

        queue.Enqueue(person1);
        queue.Enqueue(person2);
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal(2, queue.Count);
        var peeked = queue.Peek();
        Assert.Equal("John", peeked.Name);

        // 法案 - 删除
        var dequeued = queue.Dequeue();
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Single(queue);
        Assert.Equal("John", dequeued.Name);
        Assert.Equal(30, dequeued.Age);
    }

    /// <summary>
    /// 测试清除操作,该操作从队列中删除所有项目。
    /// 证明清楚操作已正确日记。
    /// </summary>
    [Fact]
    public async Task DurableQueue_Clear_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var queue = new DurableQueue<string>("clearQueue", manager, new OrleansBinaryDurableQueueCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加项目
        queue.Enqueue("one");
        queue.Enqueue("two");
        queue.Enqueue("three");
        await manager.WriteStateAsync(CancellationToken.None);

        // 动作 - 清除
        queue.Clear();
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Empty(queue);
        Assert.Empty(queue);
    }

    /// <summary>
    /// 测试在空队列上操作的边缘大小写 。
    /// 证明Peek和Dequeue提出适当的例外
    /// 当队列为空时。
    /// </summary>
    [Fact]
    public async Task DurableQueue_EmptyQueueOperations_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var queue = new DurableQueue<string>("emptyQueue", manager, new OrleansBinaryDurableQueueCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Empty(queue);

        // 动作和 Assert - 空队列上的比克和脱克应丢弃
        Assert.Throws<InvalidOperationException>(() => queue.Peek());
        Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
    }

    /// <summary>
    /// 测试队列查查能力。
    /// 验证队列支持标准查点模式
    /// 返回 FIFO 顺序中的条目。
    /// </summary>
    [Fact]
    public async Task DurableQueue_Enumeration_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<string>();
        var queue = new DurableQueue<string>("enumQueue", manager, new OrleansBinaryDurableQueueCommandCodec<string>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加项目
        var expectedItems = new List<string> { "one", "two", "three" };

        foreach (var item in expectedItems)
        {
            queue.Enqueue(item);
        }

        await manager.WriteStateAsync(CancellationToken.None);

        // 法案法
        var actualItems = queue.ToList();

        // 发还 - 项目的顺序应与排在前的顺序相同
        Assert.Equal(expectedItems, actualItems);
    }

    /// <summary>
    /// 大量物品排队后应激反应测试。
    /// 测试日记系统能够处理数千个队列操作
    /// 并正确恢复整个队列状态。
    /// </summary>
    [Fact]
    public async Task DurableQueue_LargeNumberOfOperations_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<int>();
        var queue = new DurableQueue<int>("largeQueue", manager, new OrleansBinaryDurableQueueCommandCodec<int>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 动作 - 附加许多项目
        const int itemCount = 1000;
        for (int i = 0; i < itemCount; i++)
        {
            queue.Enqueue(i);
        }

        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal(itemCount, queue.Count);
        Assert.Equal(0, queue.Peek());

        // 创建一个具有相同存储器的新管理器, 用于测试回收
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var queue2 = new DurableQueue<int>("largeQueue", sut2.Manager, new OrleansBinaryDurableQueueCommandCodec<int>(codec, SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 发还 - 正确恢复大队列
        Assert.Equal(itemCount, queue2.Count);

        // - 取消所有物品的清关并核实命令
        for (int i = 0; i < itemCount; i++)
        {
            var item = queue2.Dequeue();
            Assert.Equal(i, item);
        }

        await sut2.Manager.WriteStateAsync(CancellationToken.None);
        Assert.Empty(queue2);
    }

    /// <summary>
    /// 测试复杂的队列操作模式,使用间断式排队/脱队操作。
    /// 模拟在项目分批添加和删除时的真实世界使用情况,
    /// 校验队列是否维持正确的顺序和状态 。
    /// </summary>
    [Fact]
    public async Task DurableQueue_Concurrent_EnqueueDequeue_Test()
    {
        // 排列排列
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<int>();
        var queue = new DurableQueue<int>("concurrentQueue", manager, new OrleansBinaryDurableQueueCommandCodec<int>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 动作 - 模拟同时操作的队列
        const int batchSize = 100;

        // 第一批:增加100件
        for (int i = 0; i < batchSize; i++)
        {
            queue.Enqueue(i);
        }
        await manager.WriteStateAsync(CancellationToken.None);

        // 删除 50 项
        for (int i = 0; i < batchSize / 2; i++)
        {
            queue.Dequeue();
        }
        await manager.WriteStateAsync(CancellationToken.None);

        // 添加其他100项
        for (int i = batchSize; i < batchSize * 2; i++)
        {
            queue.Enqueue(i);
        }
        await manager.WriteStateAsync(CancellationToken.None);

        // 发货
        Assert.Equal(batchSize + batchSize / 2, queue.Count); // 应有150件物品

        // 创建一个具有相同存储器的新管理器, 用于测试回收
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var queue2 = new DurableQueue<int>("concurrentQueue", sut2.Manager, new OrleansBinaryDurableQueueCommandCodec<int>(codec, SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 摊销 - 应当以正确的状态和订购来恢复队列
        Assert.Equal(batchSize + batchSize / 2, queue2.Count);

        // 第一批的最初值应为第一批的后半批
        for (int i = batchSize / 2; i < batchSize; i++)
        {
            var item = queue2.Dequeue();
            Assert.Equal(i, item);
        }

        // 那第二批就拿第二批
        for (int i = batchSize; i < batchSize * 2; i++)
        {
            var item = queue2.Dequeue();
            Assert.Equal(i, item);
        }

        await sut2.Manager.WriteStateAsync(CancellationToken.None);
        Assert.Empty(queue2);
    }
}
