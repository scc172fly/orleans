using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TestExtensions;
using Xunit;

#nullable enable

namespace NonSilo.Tests;

[TestCategory("BVT"), TestCategory("Lifecycle")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Runtime")]
public class ServiceLifecycleTests
{
    private readonly ServiceLifecycle<ISiloLifecycle> _lifecycle;
    private readonly CancelableSiloLifecycleSubject _subject;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public ServiceLifecycleTests(ITestOutputHelper output)
    {
        var factory = new LoggerFactory([new XunitLoggerProvider(output)]);

        _subject = new CancelableSiloLifecycleSubject(factory.CreateLogger<SiloLifecycleSubject>());
        _lifecycle = new ServiceLifecycle<ISiloLifecycle>(factory.CreateLogger<ServiceLifecycle<ISiloLifecycle>>());

        _lifecycle.Participate(_subject);
    }

    private static (Task<object?> Task, IDisposable Registration) RegisterCallback(
        IServiceLifecycleStage stage,
        Action<object?, CancellationToken>? action = null,
        object? state = null,
        bool terminateOnError = true)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var registration = stage.Register((s, ct) =>
        {
            try
            {
                action?.Invoke(s, ct);
                tcs.TrySetResult(s);
            }
            catch (Exception ex)
            {
                // 在 TCS 上设置异常，使测试可以检查此回调的具体失败情况。
                tcs.TrySetException(ex);

                // 重新引发异常，使 LifecycleSubject 按 TerminateOnError 的配置运行。
                throw;
            }
            return Task.CompletedTask;
        }, state, terminateOnError);

        return (tcs.Task, registration);
    }

    [Fact]
    public async Task BasicCallbackExecution()
    {
        var callbackState = "test-state";

        var (task, _) = RegisterCallback(_lifecycle.Started, (state, ct) => { }, callbackState);

        await _subject.OnStart(TestContext.Current.CancellationToken);

        var result = await task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(callbackState, result);
    }

    [Fact]
    public async Task Stage_WaitAsync()
    {
        var waitTask = _lifecycle.Started.WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(waitTask.IsCompleted);

        await _subject.OnStart(TestContext.Current.CancellationToken);
        await waitTask.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Stage_WaitAsync_Cancellation()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // 注册一个会阻塞的回调，使阶段保持“运行中”状态。
        // 这会强制 WaitAsync 实际阻塞，使我们能够验证取消
        // 令牌时是否按预期中断等待。

        _lifecycle.Started.Register((_, _) => tcs.Task);

        var startTask = _subject.OnStart(TestContext.Current.CancellationToken);
        var waitTask = _lifecycle.Started.WaitAsync(cts.Token);

        Assert.False(waitTask.IsCompleted, "WaitAsync should be paused waiting for the stage to complete");

        await cts.CancelAsync();
        await Assert.ThrowsAsync<TaskCanceledException>(() => waitTask);

        tcs.SetResult();

        await startTask;
    }

    [Fact]
    public async Task Stage_NotifyCompleted_IsIdempotent()
    {
        var stage = new ServiceLifecycleNotificationStage(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, "Started");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionCount = 0;

        stage.Register(async (_, _) =>
        {
            Interlocked.Increment(ref executionCount);
            await gate.Task;
        }, state: null, terminateOnError: true);

        var first = stage.NotifyCompleted(CancellationToken.None);
        var second = stage.NotifyCompleted(CancellationToken.None);

        Assert.False(second.IsCompleted);

        gate.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Timeout, TestContext.Current.CancellationToken);

        await stage.NotifyCompleted(TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task CallbackDisposal_PreventsExecution()
    {
        var (task, registration) = RegisterCallback(_lifecycle.Stopping);

        registration.Dispose();

        await _subject.OnStart(TestContext.Current.CancellationToken);
        await _subject.OnStop(TestContext.Current.CancellationToken);

        Assert.False(task.IsCompleted);
    }

    [Fact]
    public async Task CancellationToken_TriggeredOnStageCompletion()
    {
        var tcs = new TaskCompletionSource();

        using var registration = _lifecycle.Stopping.Token.Register(tcs.SetResult);

        await _subject.OnStart(TestContext.Current.CancellationToken);
        await _subject.OnStop(TestContext.Current.CancellationToken);

        await tcs.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ErrorHandling_TerminateOnErrorFalse()
    {
        var (task, _) = RegisterCallback(
        _lifecycle.Started,
        (state, ct) => throw new InvalidOperationException("Test"),
        terminateOnError: false);

        await _subject.OnStart(TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => task.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.Equal("Test", ex.Message);
    }

    [Fact]
    public async Task ErrorHandling_TerminateOnErrorTrue()
    {
        var (task, _) = RegisterCallback(
            _lifecycle.Started,
            (state, ct) => throw new InvalidOperationException("Test"),
            terminateOnError: true);

        var startTask = _subject.OnStart(TestContext.Current.CancellationToken);

        // 这确保回调实际执行过，而不是仅捕获生命周期中止。
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => task.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.Equal("Test", ex.Message);

        // 现在验证生命周期启动是否按预期失败。
        await Assert.ThrowsAsync<InvalidOperationException>(() => startTask);
    }

    [Fact]
    public async Task ErrorHandling_TerminateOnErrorTrue_MultipleFailures()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (task1, _) = RegisterCallback(
            _lifecycle.Started,
            (_, _) => throw new InvalidOperationException("first"),
            terminateOnError: true);

        var (task2, _) = RegisterCallback(
            _lifecycle.Started,
            (_, _) => throw new ArgumentException("second"),
            terminateOnError: true);

        var startTask = _subject.OnStart(cancellationToken);

        // 先捕获并忽略启动异常，以便检查各个任务。
        try
        {
            await startTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 忽略。
        }

        // 现在等待两个 TCS 信号完成（更准确地说是失败），然后再进行断言。
        // 这可以防止 OnStart 异常传播与 TCS 设置之间出现竞态。

        var firstException = await Record.ExceptionAsync(() => task1.WaitAsync(Timeout, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        Assert.IsType<InvalidOperationException>(firstException);

        var secondException = await Record.ExceptionAsync(() => task2.WaitAsync(Timeout, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        Assert.IsType<ArgumentException>(secondException);
    }

    [Fact]
    public async Task LateRegistration_ExecutedImmediately()
    {
        await _subject.OnStart(TestContext.Current.CancellationToken);
        await _subject.OnStop(TestContext.Current.CancellationToken);

        // 阶段完成后进行的注册应立即运行
        var (task, _) = RegisterCallback(_lifecycle.Stopping);

        await task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConcurrentCallbacks_RegistrationSafe()
    {
        const int Count = 50;

        var startSignal = new ManualResetEventSlim(false);
        var tasks = new Task[Count];
        var executionCount = 0;

        for (var i = 0; i < Count; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                startSignal.Wait(TestContext.Current.CancellationToken);
                RegisterCallback(_lifecycle.Started, (_, _) => Interlocked.Increment(ref executionCount));
            }, TestContext.Current.CancellationToken);
        }

        startSignal.Set();

        await Task.WhenAll(tasks);
        await _subject.OnStart(TestContext.Current.CancellationToken);

        Assert.Equal(Count, executionCount);
    }

    [Fact]
    public async Task MultipleStages_ExecuteInOrder()
    {
        var executionOrder = new ConcurrentQueue<string>();

        RegisterCallback(_lifecycle.Started, (_, _) => executionOrder.Enqueue("Started"));
        RegisterCallback(_lifecycle.Stopping, (_, _) => executionOrder.Enqueue("Stopping"));

        // 捕获停止任务，以便专门等待它。
        var (stoppedTask, _) = RegisterCallback(_lifecycle.Stopped, (_, _) => executionOrder.Enqueue("Stopped"));

        await _subject.OnStart(TestContext.Current.CancellationToken);
        await _subject.OnStop(TestContext.Current.CancellationToken);
        await stoppedTask.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        var order = executionOrder.ToArray();

        Assert.Equal(3, order.Length);
        Assert.Equal("Started", order[0]);
        Assert.Equal("Stopping", order[1]);
        Assert.Equal("Stopped", order[2]);
    }

    [Fact]
    public async Task BackgroundWorker_StopsOnCancellation()
    {
        var workerExited = new TaskCompletionSource();
        var token = _lifecycle.Stopping.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                workerExited.SetResult();
            }
        }, TestContext.Current.CancellationToken);

        await _subject.OnStart(TestContext.Current.CancellationToken);
        await _subject.OnStop(TestContext.Current.CancellationToken);

        await workerExited.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Lifecycle_CancellationToken_PassedToCallback()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tcs = new TaskCompletionSource<bool>();

        // 这里手动注册，因为逻辑专门涉及回调内的取消令牌处理，
        // 而且返回的 Task 结果与标准流程不同。
        _lifecycle.Started.Register(async (state, ct) =>
        {
            try
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                tcs.SetResult(true);
            }
        });

        var startTask = _subject.OnStart(cancellationToken);

        await _subject.CancelStartAsync();

        try
        {
            await startTask;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {

        }

        var tokenWasCancelled = await tcs.Task.WaitAsync(Timeout, cancellationToken);
        Assert.True(tokenWasCancelled);
    }

    /// <summary>
    /// 用于测试取消行为的实际主体对象的简易可取消版本。
    /// </summary>
    public class CancelableSiloLifecycleSubject(ILogger<SiloLifecycleSubject> logger) : SiloLifecycleSubject(logger)
    {
        private readonly CancellationTokenSource _cts = new();

        public override Task OnStart(CancellationToken cancellationToken = default)
        {
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            return base.OnStart(linkedCts.Token);
        }

        public Task CancelStartAsync()
        {
            _cts.Cancel();
            return Task.CompletedTask;
        }
    }
}
