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
/// 测试对返回 <see cref="IAsyncEnumerable{T}"/> 的 Grain 方法的支持。
/// 这些测试验证 Orleans 处理 Grain 方法流式结果的能力，
/// 包括批处理、错误处理、取消和正确的资源清理。
/// Orleans 使用 Grain 扩展机制，在分布式系统中管理
/// 异步枚举器的生命周期。
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
    /// 测试基本异步枚举功能，其中 Grain 生成由客户端消费的值。
    /// 验证值能够正确传输，并且枚举器使用完毕后得到正确释放。
    /// 这展示了 Orleans 支持从 Grain 流式传输数据，而无需将全部数据保留在内存中。
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

        // 检查枚举器是否已释放。
        var grainCalls = await grain.GetIncomingCalls();
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 测试异步枚举流在产生任何值之前的提前取消。
    /// 验证当 CancellationToken 在枚举开始产生值之前被取消时，
    /// 操作会正确引发 OperationCanceledException，且不返回任何值。
    /// 此测试确保 Orleans 能妥善处理分布式流式传输场景中的提前取消。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("Observable")]
    public async Task ObservableGrain_AsyncEnumerable_CancelBeforeYield()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = GrainFactory.GetGrain<IObservableGrain>(Guid.NewGuid());

        // 设置取消令牌，一个用于测试超时，另一个用于 Grain 调用
        using var testCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        testCts.CancelAfter(TimeSpan.FromSeconds(35));
        using var callCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var callId = Guid.NewGuid();

        // 用于在枚举开始之后、产生值之前取消枚举的任务
        var enumerationStartedTask = Task.Run(async () =>
        {
            await grain.WaitForCall(callId); // 等待枚举开始
            callCts.Cancel(); // 在产生任何值之前取消
        }, cancellationToken);

        try
        {
            // 使用运行缓慢的 Grain 方法及未取消的令牌开始枚举
            await foreach (var entry in grain.SleepyEnumerable(callId, TimeSpan.FromSeconds(25), callCts.Token))
            {
                Assert.Fail("Should have thrown due to cancellation before yielding any values.");
            }

            Assert.Fail("Enumeration should not have completed without an exception.");
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 验证取消令牌确实已取消
            Assert.True(callCts.Token.IsCancellationRequested);
        }

        await enumerationStartedTask;

        // 等待 Grain 记录取消操作
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
    /// 测试异步枚举流在枚举期间引发异常时的错误处理。
    /// 验证异常正确传播至客户端，并清理资源。
    /// errorIndex 参数决定错误发生的时机，涵盖立即发生和延迟发生的错误。
    /// waitAfterYield 参数测试产生值后存在或不存在异步延迟时的错误处理。
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

        // 检查枚举器是否已释放。
        var grainCalls = await grain.GetIncomingCalls();
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 测试 Grain 取消枚举时异步枚举流的取消处理。
    /// 验证 OperationCanceledException 正确传播，并清理资源。
    /// 此测试验证 Orleans 在分布式流式传输场景中处理协作式取消的能力。
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
        // 此特殊错误消息被解释为指示：
        // 在到达该索引时应取消操作。
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

        // 检查枚举器是否已释放。
        var grainCalls = await grain.GetIncomingCalls();
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 测试使用 CancellationToken 在客户端取消异步枚举流。
    /// 验证客户端发出的取消请求得到正确处理，包括：
    /// - 提前取消（枚举开始之前）
    /// - 流式传输中途取消（枚举期间）
    /// - 正确清理并释放服务器端资源
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
            // 检查枚举器未被释放，因为它被提前取消，因此本不应发出任何调用。
            var grainCalls = await grain.GetIncomingCalls();
            Assert.DoesNotContain(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.StartEnumeration)));
            Assert.DoesNotContain(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.DisposeAsync)));
        }
        if (errorIndex > 0)
        {
            // 检查枚举器是否已释放，但仅适用于未提前取消的情况。
            var grainCalls = await grain.GetIncomingCalls();
            Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.StartEnumeration)));
            Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.DisposeAsync)));
        }
    }

    /// <summary>
    /// 测试使用 WithCancellation 扩展方法在客户端取消。
    /// 与 CancellationToken 测试类似，但采用扩展方法取消。
    /// 验证 WithCancellation 扩展能正确集成 Orleans 的异步枚举支持。
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
            // 检查枚举器未被释放，因为它被提前取消，因此本不应发出任何调用。
            var grainCalls = await grain.GetIncomingCalls();
            Assert.DoesNotContain(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.StartEnumeration)));
            Assert.DoesNotContain(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.DisposeAsync)));
        }
        if (errorIndex > 0)
        {
            // 检查枚举器是否已释放，但仅适用于未提前取消的情况。
            var grainCalls = await grain.GetIncomingCalls();
            Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.StartEnumeration)));
            Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncEnumerableGrainExtension.DisposeAsync)));
        }
    }

    /// <summary>
    /// 测试异步枚举流的批处理优化。
    /// 验证 Orleans 能够自动将多个值合并为批次，减少网络往返次数。
    /// 流式传输大量小值时，这项优化对性能至关重要。
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

        // 检查枚举器是否已释放。
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 测试异步枚举流的自定义批次大小配置。
    /// 验证 WithBatchSize 扩展方法能正确控制每批的项数。
    /// 客户端可以借此调整延迟与吞吐量之间的权衡。
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

        // 检查枚举器是否已释放。
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 测试将批次大小设置为 1 以禁用批处理。
    /// 验证禁用批处理时，每个值都会产生一次独立的网络调用。
    /// 此模式提供最低延迟，但在流式传输场景中开销最大。
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

        // 每个值对应一次调用，最后再调用一次以完成枚举
        Assert.Equal(values.Count + 1, moveNextCallCount);

        // 检查枚举器是否已释放。
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 测试在枚举进行期间取消操作。
    /// 验证流式传输中途取消能正确停止枚举并清理资源。
    /// 这模拟客户端需要提前停止消费数据的实际场景。
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
            // 符合预期
        }

        Assert.Equal(3, values.Count);

        // 检查枚举器是否已释放。
        var grainCalls = await grain.GetIncomingCalls();
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 测试 Grain 缓慢生成数据时的异步枚举行为。
    /// 验证客户端可以在所有值生成之前停止消费。
    /// 此测试验证 Orleans 在流式传输场景中处理背压及提前终止的能力。
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

        // 检查枚举器是否已释放。
        var grainCalls = await grain.GetIncomingCalls();
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 测试客户端缓慢消费数据时的异步枚举行为。
    /// 验证客户端缓慢消费时，枚举器不会被过早清理。
    /// 使用诊断侦听器监视清理定时器的行为。
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

        // 检查枚举器是否已释放。
        var grainCalls = await grain.GetIncomingCalls();
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 测试客户端消费速度过慢时的枚举器逐出。
    /// 验证 Orleans 在超过超时时长后能正确清理被遗弃的枚举器。
    /// 这可防止客户端未完成枚举时发生资源泄漏。
    /// 测试确保在枚举器逐出后尝试继续时能够正确处理错误。
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

        // 检查枚举器是否已释放。
        var grainCalls = await grain.GetIncomingCalls();
        Assert.Contains(grainCalls, c => c.InterfaceName.Contains(nameof(IAsyncEnumerableGrainExtension)) && c.MethodName.Contains(nameof(IAsyncDisposable.DisposeAsync)));
    }

    /// <summary>
    /// 测试枚举期间 Grain 被停用时的异步枚举行为。
    /// 验证 Grain 停用会通过适当的错误正确终止正在进行的枚举。
    /// 这可确保干净地关闭，并防止 Grain 停用时客户端挂起。
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

        // 在回调内部报告清理操作，且发生在一次性定时器重新启用之前。
        await _fixture.WaitForTimerChangeAsync(timer, timerChangeCount + 1, cancellationToken);
    }

    /// <summary>
    /// 使用模拟 Silo 时间提供程序的测试夹具，使清理定时器可以确定性地推进。
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
    /// 用于在测试期间监视 AsyncEnumerableGrainExtension 行为的诊断侦听器。
    /// 此辅助类允许测试观察内部清理操作，并验证
    /// 枚举器是否按其生命周期要求得到正确管理。
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
