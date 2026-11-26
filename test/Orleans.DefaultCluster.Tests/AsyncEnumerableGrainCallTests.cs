#nullable enable
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Internal;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests;

public static class AsyncEnumerableGrainCallTestCollection
{
    public const string Name = nameof(AsyncEnumerableGrainCallTests);
}

[CollectionDefinition(AsyncEnumerableGrainCallTestCollection.Name)]
public sealed class AsyncEnumerableGrainCallTestCollectionDefinition : ICollectionFixture<AsyncEnumerableGrainCallTests.Fixture>
{
}

/// <summary>
/// 测试支持 grain 返回返回的方法 <see cref="IAsyncEnumerable{T}"/>.
/// 这些测试核实 Orleans处理 grain 方法流出结果的能力,
/// 包括批量、处理错误、注销和适当资源清理。
/// Orleans 使用 grain 扩展机制管理同步点算器的生命周期
/// 分布在分布式系统各处。
/// </summary>
[Collection(AsyncEnumerableGrainCallTestCollection.Name)]
public class AsyncEnumerableGrainCallTests
{
    private readonly Fixture _fixture;

    public AsyncEnumerableGrainCallTests(Fixture fixture)
    {
        _fixture = fixture;
    }

    private IGrainFactory GrainFactory => _fixture.GrainFactory;

    private ILogger Logger => _fixture.Logger;

    /// <summary>
    /// 测试一个 grain 产生客户消费值的可计算功能的基本同步功能。
    /// 证明数值的传输正确,使用之后对数字进行妥善处理。
    /// 这显示了 Orleans对从 grains 流出的数据的支持,而没有将所有数据保留在内存中。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("Observable")]
    public async Task ObservableGrain_AsyncEnumerable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());

        var producer = Task.Run(async () =>
        {
            foreach (var value in Enumerable.Range(0, 5))
            {
                await Task.Delay(200, cancellationToken);
                await grain.OnNext(value.ToString());
            }

            await grain.Complete();
        }, cancellationToken);

        var values = new List<string>();
        await foreach (var entry in grain.GetValues(cancellationToken).WithCancellation(cancellationToken))
        {
            values.Add(entry);
            Logger.LogInformation("ObservableGrain_AsyncEnumerable: {Entry}", entry);
        }

        Assert.Equal(5, values.Count);

        // 请检查是否对分子进行了处理
        var grainCalls = await grain.GetIncomingCalls();
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 在产生任何值之前先先行取消同声流的测试。
    /// 证明在计数开始产生值之前取消一个 CancellationToken 时,
    /// 正确丢弃 OperationCanceledException ,且不返回任何数值。
    /// 此测试确保 Orleans 在分布式流景中优雅地处理早期取消。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("Observable")]
    public async Task ObservableGrain_AsyncEnumerable_CancelBeforeYield()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());

        // 设置取消标记牌 - 一个用于测试超时,一个用于 grain 调用
        using var testCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        testCts.CancelAfter(TimeSpan.FromSeconds(35));
        using var callCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var callId = Guid.NewGuid();

        // 任务, 以取消开始后但在产生前的点数
        var enumerationStartedTask = Task.Run(async () =>
        {
            await grain.WaitForCall(callId); // 等待开始点数
            callCts.Cancel(); // 在任何值被产生之前取消
        }, cancellationToken);

        try
        {
            // 以慢速 grain 方法和未取消的牌照开始计数
            await foreach (var entry in grain.SleepyEnumerable(callId, TimeSpan.FromSeconds(25), callCts.Token))
            {
                Assert.Fail("Should have thrown due to cancellation before yielding any values.");
            }

            Assert.Fail("Enumeration should not have completed without an exception.");
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 校验取消的代号已确实被注销
            Assert.True(callCts.Token.IsCancellationRequested);
        }

        await enumerationStartedTask;

        // 等待 grain 记录取消
        while (true)
        {
            var canceledCalls = await grain.GetCanceledCalls();
            if (canceledCalls.Contains(callId))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
            if (testCts.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Fail("Test timed out waiting for cancellation to be recorded.");
            }
        }
    }

    /// <summary>
    /// 当在点查时出现例外时, 测试会处理同步可数流中的错误 。
    /// 证明例外情况已适当地向客户宣传,资源也得到清理。
    /// errorIndex 参数确定出错发生的时间,同时测试即时和延迟错误。
    /// waitAfterYield 参数测试错误处理,在产生值后有和没有同步延迟。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Theory, TestCategory("BVT"), TestCategory("Observable")]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(9, false)]
    [InlineData(9, true)]
    [InlineData(10, false)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(11, true)]
    public async Task ObservableGrain_AsyncEnumerable_Throws(int errorIndex, bool waitAfterYield)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string ErrorMessage = "This is my error!";
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());

        var values = new List<int>();
        try
        {
            await foreach (var entry in grain.GetValuesWithError(errorIndex, waitAfterYield, ErrorMessage, cancellationToken).WithBatchSize(10).WithCancellation(cancellationToken))
            {
                values.Add(entry);
                Logger.LogInformation("ObservableGrain_AsyncEnumerable: {Entry}", entry);
            }
        }
        catch (InvalidOperationException iox)
        {
            Assert.Equal(ErrorMessage, iox.Message);
        }

        Assert.Equal(errorIndex, values.Count);

        // 请检查是否对分子进行了处理
        var grainCalls = await grain.GetIncomingCalls();
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 当 grain 取消计数时, 在同步可列流中测试注销处理 。
    /// 证明 OperationCanceledException 的传播得当,资源被清理干净。
    /// 测试 Orleans处理分布式流景情景中合作取消的能力。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Theory, TestCategory("BVT"), TestCategory("Observable")]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(9, false)]
    [InlineData(9, true)]
    [InlineData(10, false)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(11, true)]
    public async Task ObservableGrain_AsyncEnumerable_Cancellation(int errorIndex, bool waitAfterYield)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // 此特殊错误消息被解释为表示取消
        // 应在达到指数时进行。
        const string ErrorMessage = "cancel";
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());

        var values = new List<int>();
        try
        {
            await foreach (var entry in grain.GetValuesWithError(errorIndex, waitAfterYield, ErrorMessage, cancellationToken).WithBatchSize(10).WithCancellation(cancellationToken))
            {
                values.Add(entry);
                Logger.LogInformation("ObservableGrain_AsyncEnumerable: {Entry}", entry);
            }
        }
        catch (OperationCanceledException oce)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedMessage = new OperationCanceledException().Message;
            Assert.Equal(expectedMessage, oce.Message);
        }

        Assert.Equal(errorIndex, values.Count);

        // 请检查是否对分子进行了处理
        var grainCalls = await grain.GetIncomingCalls();
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 使用 CancellationToken测试客户端取消可同步的可数流。
    /// 证明客户提出的取消申请得到妥善处理,包括:
    /// - 提前取消(在计数开始之前)
    /// - 中途取消(在列举期间)
    /// - 适当清理和处置服务器端资源
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Theory, TestCategory("BVT"), TestCategory("Observable")]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(9, false)]
    [InlineData(9, true)]
    [InlineData(10, false)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(11, true)]
    public async Task ObservableGrain_AsyncEnumerable_CancellationToken(int errorIndex, bool waitAfterYield)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string ErrorMessage = "Throwing!";
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());

        var values = new List<int>();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (errorIndex == 0)
            {
                cts.Cancel();
            }

            await foreach (var entry in grain.GetValuesWithError(int.MaxValue, waitAfterYield, ErrorMessage, cts.Token).WithBatchSize(10))
            {
                values.Add(entry);
                if (values.Count == errorIndex)
                {
                    cts.Cancel();
                }

                Logger.LogInformation("ObservableGrain_AsyncEnumerable: {Entry}", entry);
            }
        }
        catch (OperationCanceledException oce)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedMessage = new OperationCanceledException().Message;
            Assert.Equal(expectedMessage, oce.Message);
        }

        Assert.Equal(errorIndex, values.Count);

        if (errorIndex == 0)
        {
            // 检查该查点员是否由于事先取消而没有处理,因此本不应打电话。
            var grainCalls = await grain.GetIncomingCalls();
            Assert.DoesNotContain(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.StartEnumeration)));
            Assert.DoesNotContain(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.DisposeAsync)));
        }
        if (errorIndex > 0)
        {
            // 检查是否对分子进行了处置,但前提是它不是先发制人地被注销。
            var grainCalls = await grain.GetIncomingCalls();
            Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.StartEnumeration)));
            Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.DisposeAsync)));
        }
    }

    /// <summary>
    /// 使用 WithCancellation 扩展方法测试客户端取消。
    /// 与 CancellationToken 测试类似,但使用扩展方法取消。
    /// 证明 WithCancellation 扩展名与 Orleans的“合成数列支持”相融合适当。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Theory, TestCategory("BVT"), TestCategory("Observable")]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(9, false)]
    [InlineData(9, true)]
    [InlineData(10, false)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(11, true)]
    public async Task ObservableGrain_AsyncEnumerable_CancellationToken_WithCancellationExtension(int errorIndex, bool waitAfterYield)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string ErrorMessage = "Throwing!";
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());

        var values = new List<int>();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (errorIndex == 0)
            {
                cts.Cancel();
            }

            await foreach (var entry in grain.GetValuesWithError(int.MaxValue, waitAfterYield, ErrorMessage, cancellationToken).WithBatchSize(10).WithCancellation(cts.Token))
            {
                values.Add(entry);
                if (values.Count == errorIndex)
                {
                    cts.Cancel();
                }

                Logger.LogInformation("ObservableGrain_AsyncEnumerable: {Entry}", entry);
            }
        }
        catch (OperationCanceledException oce)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedMessage = new OperationCanceledException().Message;
            Assert.Equal(expectedMessage, oce.Message);
        }

        Assert.Equal(errorIndex, values.Count);

        if (errorIndex == 0)
        {
            // 检查该查点员是否由于事先取消而没有处理,因此本不应打电话。
            var grainCalls = await grain.GetIncomingCalls();
            Assert.DoesNotContain(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.StartEnumeration)));
            Assert.DoesNotContain(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.DisposeAsync)));
        }
        if (errorIndex > 0)
        {
            // 检查是否对分子进行了处置,但前提是它不是先发制人地被注销。
            var grainCalls = await grain.GetIncomingCalls();
            Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.StartEnumeration)));
            Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.DisposeAsync)));
        }
    }

    /// <summary>
    /// 测试对同步可数流的批量优化。
    /// 证明 Orleans 自动分批多个值以减少网络圆程。
    /// 这种优化对于在流出许多小值时的绩效至关重要。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("Observable")]
    public async Task ObservableGrain_AsyncEnumerable_Batch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());

        foreach (var value in Enumerable.Range(0, 50))
        {
            await grain.OnNext(value.ToString());
        }

        await grain.Complete();

        var values = new List<string>();
        await foreach (var entry in grain.GetValues(cancellationToken).WithCancellation(cancellationToken))
        {
            values.Add(entry);
            Logger.LogInformation("ObservableGrain_AsyncEnumerable: {Entry}", entry);
        }

        Assert.Equal(50, values.Count);

        var grainCalls = await grain.GetIncomingCalls();
        var moveNextCallCount = grainCalls.Count(element =>
            element.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension))
            && (element.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.MoveNext)) || element.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.StartEnumeration))));
        Assert.True(moveNextCallCount < values.Count);

        // 请检查是否对分子进行了处理
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 测试对同步可数流的批量大小配置自定义 。
    /// 证明 WithBatchSize 扩展方法正确控制了每批物品的数量。
    /// 这使客户能够调整延缓和吞吐之间的权衡。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("Observable")]
    public async Task ObservableGrain_AsyncEnumerable_SplitBatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());

        foreach (var value in Enumerable.Range(0, 50))
        {
            await grain.OnNext(value.ToString());
        }

        await grain.Complete();

        var values = new List<string>();
        await foreach (var entry in grain.GetValues(cancellationToken).WithBatchSize(25).WithCancellation(cancellationToken))
        {
            values.Add(entry);
            Logger.LogInformation("ObservableGrain_AsyncEnumerable: {Entry}", entry);
        }

        Assert.Equal(50, values.Count);

        var grainCalls = await grain.GetIncomingCalls();
        var moveNextCallCount = grainCalls.Count(element =>
            element.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension))
            && (element.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.MoveNext)) || element.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.StartEnumeration))));
        Assert.True(moveNextCallCount < values.Count);

        // 请检查是否对分子进行了处理
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 将批量大小设定为 1 的测试使分批处理失效。
    /// 证明在批量被禁用时,每个值都产生单独的网络呼叫。
    /// 这一模式为流出情景提供了最低的潜伏,但最高的管理费用。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("Observable")]
    public async Task ObservableGrain_AsyncEnumerable_NoBatching()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());

        foreach (var value in Enumerable.Range(0, 50))
        {
            await grain.OnNext(value.ToString());
        }

        await grain.Complete();

        var values = new List<string>();
        await foreach (var entry in grain.GetValues(cancellationToken).WithBatchSize(1).WithCancellation(cancellationToken))
        {
            values.Add(entry);
            Logger.LogInformation("ObservableGrain_AsyncEnumerable: {Entry}", entry);
        }

        Assert.Equal(50, values.Count);

        var grainCalls = await grain.GetIncomingCalls();
        var moveNextCallCount = grainCalls.Count(element =>
            element.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension))
            && (element.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.MoveNext)) || element.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.StartEnumeration))));

        // 一次要求每个价值,最后一次要求完成点数
        Assert.Equal(values.Count + 1, moveNextCallCount);

        // 请检查是否对分子进行了处理
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 主动点名时取消测试。
    /// 证明取消中流,适当停止查点和清理资源。
    /// 这模拟了现实世界的情景 客户需要及早停止消费数据
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("Observable")]
    public async Task ObservableGrain_AsyncEnumerable_WithCancellation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());

        var producer = Task.Run(async () =>
        {
            foreach (var value in Enumerable.Range(0, 5))
            {
                await Task.Delay(200, cancellationToken);
                await grain.OnNext(value.ToString());
            }

            await grain.Complete();
        }, cancellationToken);

        var values = new List<string>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            await foreach (var entry in grain.GetValues(cancellationToken).WithCancellation(cts.Token))
            {
                values.Add(entry);
                if (values.Count == 3)
                {
                    cts.Cancel();
                }

                Logger.LogInformation("ObservableGrain_AsyncEnumerable: {Entry}", entry);
            }

            Assert.Fail("Expected an exception to be thrown");
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 预期预期预计预计预计预计预计预计的预期预计预计预计预计的预期预计预计预计的
        }

        Assert.Equal(3, values.Count);

        // 请检查是否对分子进行了处理
        var grainCalls = await grain.GetIncomingCalls();
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 测试慢产 grain的可数行为。
    /// 证明客户可在产生所有价值之前停止消费。
    /// 测试 Orleans处理后压和在流态情景中提前终止的能力。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("Observable")]
    public async Task ObservableGrain_AsyncEnumerable_SlowProducer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());

        var producer = Task.Run(async () =>
        {
            foreach (var value in Enumerable.Range(0, 5))
            {
                await Task.Delay(2000, cancellationToken);
                await grain.OnNext(value.ToString());
            }

            await grain.Complete();
        }, cancellationToken);

        var values = new List<string>();
        await foreach (var entry in grain.GetValues(cancellationToken).WithCancellation(cancellationToken))
        {
            values.Add(entry);
            if (values.Count == 2)
            {
                break;
            }

            Logger.LogInformation("ObservableGrain_AsyncEnumerable: {Entry}", entry);
        }

        Assert.Equal(2, values.Count);

        // 请检查是否对分子进行了处理
        var grainCalls = await grain.GetIncomingCalls();
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 测试与消费缓慢的客户群一起发生的无数行为。
    /// 证明当客户消费缓慢时,该调查员不会过早地清理干净。
    /// 使用诊断性听众来监测清理计时器的行为。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("Observable")]
    public async Task ObservableGrain_AsyncEnumerable_SlowConsumer()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());
        using var listener = new AsyncEnumerableGrainExtensionListener(grain.GetGrainId());

        var producer = Task.Run(async () =>
        {
            foreach (var value in Enumerable.Range(0, 3))
            {
                await grain.OnNext(value.ToString());
            }

            await grain.Complete();
        }, cts.Token);

        var values = new List<string>();
        await foreach (var entry in grain.GetValues(cts.Token).WithBatchSize(1).WithCancellation(cts.Token))
        {
            values.Add(entry);

            await AdvanceToNextCleanupAsync(listener, cts.Token);
            Logger.LogInformation("ObservableGrain_AsyncEnumerable: {Entry}", entry);
        }

        Assert.Equal(3, values.Count);

        // 请检查是否对分子进行了处理
        var grainCalls = await grain.GetIncomingCalls();
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 当客户消费过慢时,测试人口统计员驱逐。
    /// 证明 Orleans 在超时后适当清理废弃的计数员。
    /// 这防止了客户未能完成查点时资源漏出。
    /// 测试确保了驱逐后继续处理错误时能够正确处理。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("Observable")]
    public async Task ObservableGrain_AsyncEnumerable_SlowConsumer_Evicted()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());
        using var listener = new AsyncEnumerableGrainExtensionListener(grain.GetGrainId());

        var producer = Task.Run(async () =>
        {
            foreach (var value in Enumerable.Range(0, 5))
            {
                await grain.OnNext(value.ToString());
            }

            await grain.Complete();
        }, cts.Token);

        var values = new List<string>();
        try
        {
            await foreach (var entry in grain.GetValues(cts.Token).WithBatchSize(1).WithCancellation(cts.Token))
            {
                values.Add(entry);

                if (values.Count == 3)
                {
                    await AdvanceToNextCleanupAsync(listener, cts.Token);
                    await AdvanceToNextCleanupAsync(listener, cts.Token);
                }

                Logger.LogInformation("ObservableGrain_AsyncEnumerable: {Entry}", entry);
            }

            Assert.Fail("Expected an exception to be thrown");
        }
        catch (EnumerationAbortedException ex)
        {
            Assert.Contains("the remote target does not have a record of this enumerator", ex.Message);
        }

        Assert.Equal(3, values.Count);

        // 请检查是否对分子进行了处理
        var grainCalls = await grain.GetIncomingCalls();
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 当 grain 在查点时被停用时,测试可数行为。
    /// 证明 grain 解除[失效]适当终止了有适当误差的主动计数。
    /// 这确保了干净的关闭,防止在 grains 被停用时吊死客户。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("Observable")]
    public async Task ObservableGrain_AsyncEnumerable_Deactivate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());
        var valuesObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var producer = Task.Run(async () =>
        {
            foreach (var value in Enumerable.Range(0, 2))
            {
                await grain.OnNext(value.ToString());
            }

            await valuesObserved.Task.WaitAsync(cancellationToken);
            await grain.Deactivate();
        }, cancellationToken);

        var values = new List<string>();
        await Assert.ThrowsAsync<EnumerationAbortedException>(async () =>
        {
            try
            {
                await foreach (var entry in grain.GetValues(cancellationToken))
                {
                    values.Add(entry);
                    if (values.Count == 2)
                    {
                        valuesObserved.TrySetResult();
                    }

                    Logger.LogInformation("ObservableGrain_AsyncEnumerable: {Entry}", entry);
                }
            }
            finally
            {
                if (values.Count < 2)
                {
                    valuesObserved.TrySetException(
                        new InvalidOperationException($"Enumeration ended after observing {values.Count} of 2 values."));
                }
            }
        }).WaitAsync(cancellationToken);

        await producer.WaitAsync(cancellationToken);
        Assert.Equal(2, values.Count);
    }

    private async Task AdvanceToNextCleanupAsync(AsyncEnumerableGrainExtensionListener listener, CancellationToken cancellationToken)
    {
        var cleanupCount = listener.CleanupCount + 1;
        var timer = listener.Timer;
        var timerChangeCount = _fixture.GetTimerChangeCount(timer);
        await _fixture.AdvanceTimeByResponseTimeoutAsync().WaitAsync(cancellationToken);
        await listener.WaitForCleanupCountAsync(cleanupCount, cancellationToken);

        // 清理报告来自回调内部 在一发计时器重开前
        await _fixture.WaitForTimerChangeAsync(timer, timerChangeCount + 1, cancellationToken);
    }

    /// <summary>
    /// 使用假 silo 时间提供者的测试固定装置,以便清理定时器能够以确定的方式先进。
    /// </summary>
    public sealed class Fixture : BaseInProcessTestClusterFixture
    {
        private readonly TrackingFakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        protected override void ConfigureTestCluster(InProcessTestClusterBuilder builder)
        {
            builder.ConfigureSilo((_, siloBuilder) =>
            {
                siloBuilder
                    .Configure<SiloMessagingOptions>(o => o.ClientGatewayShutdownNotificationTimeout = default)
                    .UseInMemoryReminderService()
                    .AddMemoryGrainStorageAsDefault()
                    .AddMemoryGrainStorage("MemoryStore");

                siloBuilder.UseFakeTimeProviderForGrainTimers(_timeProvider);
            });
        }

        public Task AdvanceTimeByResponseTimeoutAsync() =>
            FakeTimeSilo.AdvanceAsync(_timeProvider, HostedCluster.GetSiloServiceProvider().GetRequiredService<IOptions<SiloMessagingOptions>>().Value.ResponseTimeout);

        public int GetTimerChangeCount(object timer) => _timeProvider.GetTimerChangeCount(timer);

        public Task WaitForTimerChangeAsync(object timer, int changeCount, CancellationToken cancellationToken) =>
            _timeProvider.WaitForTimerChangeAsync(timer, changeCount, cancellationToken);

    }

    /// <summary>
    /// 检测期间监测 AsyncEnumerableGrainExtension 行为的诊断倾听器。
    /// 这个辅助人员班可以进行测试,观察内部清理作业和核查
    /// 计算员应根据其生命周期要求得到适当管理。
    /// </summary>
    private sealed class AsyncEnumerableGrainExtensionListener : IObserver<KeyValuePair<string, object?>>, IObserver<DiagnosticListener>, IDisposable
    {
        private readonly object _lock = new();
        private readonly IDisposable _allListenersSubscription;
        private readonly GrainId _targetGrainId;
        private IDisposable? _instanceSubscription;
        private TaskCompletionSource? _cleanupCompleted;
        private object? _timer;
        private int _cleanupCount;

        public AsyncEnumerableGrainExtensionListener(GrainId targetGrainId)
        {
            _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
            _targetGrainId = targetGrainId;
        }

        public int CleanupCount
        {
            get
            {
                lock (_lock)
                {
                    return _cleanupCount;
                }
            }
        }

        public object Timer
        {
            get
            {
                lock (_lock)
                {
                    return _timer ?? throw new InvalidOperationException("The async enumerable grain extension has not been created.");
                }
            }
        }

        public async Task WaitForCleanupCountAsync(int cleanupCount, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task cleanupCompletedTask;
                lock (_lock)
                {
                    if (_cleanupCount >= cleanupCount)
                    {
                        return;
                    }

                    _cleanupCompleted ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                    cleanupCompletedTask = _cleanupCompleted.Task;
                }

                await cleanupCompletedTask.WaitAsync(cancellationToken);
            }
        }

        void IObserver<KeyValuePair<string, object?>>.OnCompleted()
        {
            _instanceSubscription?.Dispose();
        }

        void IObserver<KeyValuePair<string, object?>>.OnError(Exception error)
        {
        }

        void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value)
        {
            var extension = (AsyncEnumerableGrainExtension)value.Value!;
            if (extension.GrainContext.GrainId != _targetGrainId)
            {
                return;
            }

            lock (_lock)
            {
                _timer ??= extension.Timer;
            }

            if (value.Key == "OnEnumeratorCleanupCompleted")
            {
                TaskCompletionSource? cleanupCompleted;
                lock (_lock)
                {
                    ++_cleanupCount;
                    cleanupCompleted = _cleanupCompleted;
                    _cleanupCompleted = null;
                }

                cleanupCompleted?.TrySetResult();
            }
        }

        void IObserver<DiagnosticListener>.OnCompleted() { }
        void IObserver<DiagnosticListener>.OnError(Exception error) { }
        void IObserver<DiagnosticListener>.OnNext(DiagnosticListener value)
        {
            if (value.Name == "Orleans.Runtime.AsyncEnumerableGrainExtension")
            {
                _instanceSubscription = value.Subscribe(this);
            }
        }

        public void Dispose()
        {
            _allListenersSubscription.Dispose();
            _instanceSubscription?.Dispose();
        }
    }
}
