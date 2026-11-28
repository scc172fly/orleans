using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains;

/// <summary>
/// 激活取消测试 Grain 的基类。
/// </summary>
public abstract class ActivationCancellationTestGrainBase : Grain
{
    protected readonly ILogger Logger;
    protected readonly string ActivationId = Guid.NewGuid().ToString();
    protected bool IsActivatedSuccessfully;
    private readonly IGrainRuntime _grainRuntime;

    protected ActivationCancellationTestGrainBase(ILogger logger, IGrainRuntime grainRuntime)
    {
        Logger = logger;
        _grainRuntime = grainRuntime;
    }

    /// <summary>
    /// 从 Grain 运行时获取 TimeProvider。
    /// </summary>
    protected TimeProvider TimeProvider => _grainRuntime.TimeProvider;

    public Task<string> GetActivationId() => Task.FromResult(ActivationId);

    public Task<bool> IsActivated() => Task.FromResult(IsActivatedSuccessfully);
}

/// <summary>
/// 在 OnActivateAsync 中，取消令牌触发时抛出 OperationCanceledException 的 Grain。
/// 模拟通过将取消令牌传递给异步方法来正确响应取消的代码。
/// </summary>
public class ActivationCancellation_ThrowsOperationCancelledGrain
    : ActivationCancellationTestGrainBase, IActivationCancellation_ThrowsOperationCancelledGrain
{
    public ActivationCancellation_ThrowsOperationCancelledGrain(ILogger<ActivationCancellation_ThrowsOperationCancelledGrain> logger, IGrainRuntime grainRuntime)
        : base(logger, grainRuntime)
    {
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("OnActivateAsync starting for {GrainType}", GetType().Name);

        // 检查是否应该模拟会导致取消的延迟
        if (RequestContext.Get("delay_activation_ms") is int delayMs && delayMs > 0)
        {
            Logger.LogDebug("Delaying activation by {DelayMs}ms", delayMs);
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
        }

        IsActivatedSuccessfully = true;
        Logger.LogDebug("OnActivateAsync completed successfully for {GrainType}", GetType().Name);
        await base.OnActivateAsync(cancellationToken);
    }
}

/// <summary>
/// 在 OnActivateAsync 中抛出 ObjectDisposedException 的 Grain。
/// 模拟不响应取消令牌却尝试访问服务的代码，
/// 而这些服务在取消后已被释放。
/// </summary>
public class ActivationCancellation_ThrowsObjectDisposedGrain
    : ActivationCancellationTestGrainBase, IActivationCancellation_ThrowsObjectDisposedGrain
{
    public ActivationCancellation_ThrowsObjectDisposedGrain(
        ILogger<ActivationCancellation_ThrowsObjectDisposedGrain> logger,
        IGrainRuntime grainRuntime)
        : base(logger, grainRuntime)
    {
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("OnActivateAsync starting for {GrainType}", GetType().Name);

        if (RequestContext.Get("delay_activation_ms") is int delayMs && delayMs > 0)
        {
            Logger.LogDebug("Setting up cancellation callback to throw ObjectDisposedException after {DelayMs}ms max", delayMs);

            var tcs = new TaskCompletionSource<bool>();

            // 注册回调，在请求取消时抛出 ObjectDisposedException
            await using var registration = cancellationToken.Register(() =>
            {
                Logger.LogDebug("Cancellation was requested, throwing ObjectDisposedException");
                tcs.TrySetException(new ObjectDisposedException("IServiceProvider", "The service provider has been disposed because the activation was cancelled."));
            });

            Thread.Sleep(TimeSpan.FromMilliseconds(delayMs));

            await tcs.Task;
        }

        IsActivatedSuccessfully = true;
        Logger.LogDebug("OnActivateAsync completed successfully for {GrainType}", GetType().Name);
        await base.OnActivateAsync(cancellationToken);
    }
}

/// <summary>
/// 在 OnActivateAsync 中抛出一般异常（与取消无关）的 Grain。
/// </summary>
public class ActivationCancellation_ThrowsGenericExceptionGrain
    : ActivationCancellationTestGrainBase, IActivationCancellation_ThrowsGenericExceptionGrain
{
    public ActivationCancellation_ThrowsGenericExceptionGrain(ILogger<ActivationCancellation_ThrowsGenericExceptionGrain> logger, IGrainRuntime grainRuntime)
        : base(logger, grainRuntime)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("OnActivateAsync starting for {GrainType}", GetType().Name);

        // 检查是否应该抛出异常
        if (RequestContext.Get("throw_exception") is bool shouldThrow && shouldThrow)
        {
            Logger.LogDebug("Throwing generic exception as requested");
            throw new InvalidOperationException("This is a test exception thrown during activation.");
        }

        IsActivatedSuccessfully = true;
        Logger.LogDebug("OnActivateAsync completed successfully for {GrainType}", GetType().Name);
        return base.OnActivateAsync(cancellationToken);
    }
}

/// <summary>
/// 顺利完成激活的 Grain。
/// </summary>
public class ActivationCancellation_SuccessfulActivationGrain
    : ActivationCancellationTestGrainBase, IActivationCancellation_SuccessfulActivationGrain
{
    public ActivationCancellation_SuccessfulActivationGrain(ILogger<ActivationCancellation_SuccessfulActivationGrain> logger, IGrainRuntime grainRuntime)
        : base(logger, grainRuntime)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("OnActivateAsync starting for {GrainType}", GetType().Name);
        IsActivatedSuccessfully = true;
        Logger.LogDebug("OnActivateAsync completed successfully for {GrainType}", GetType().Name);
        return base.OnActivateAsync(cancellationToken);
    }
}

/// <summary>
/// 在 OnActivateAsync 中抛出 TaskCanceledException 的 Grain。
/// TaskCanceledException 继承自 OperationCanceledException，应采用相同的处理方式。
/// </summary>
public class ActivationCancellation_ThrowsTaskCancelledGrain
    : ActivationCancellationTestGrainBase, IActivationCancellation_ThrowsTaskCancelledGrain
{
    public ActivationCancellation_ThrowsTaskCancelledGrain(ILogger<ActivationCancellation_ThrowsTaskCancelledGrain> logger, IGrainRuntime grainRuntime)
        : base(logger, grainRuntime)
    {
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("OnActivateAsync starting for {GrainType}", GetType().Name);

        if (RequestContext.Get("delay_activation_ms") is int delayMs && delayMs > 0)
        {
            Logger.LogDebug("Setting up cancellation callback to throw TaskCanceledException after {DelayMs}ms max", delayMs);

            var tcs = new TaskCompletionSource<bool>();

            // 注册回调，在请求取消时抛出 TaskCanceledException
            /*await using var registration = cancellationToken.Register(() =>
            {
                Logger.LogDebug("Cancellation was requested, throwing TaskCanceledException");
                tcs.TrySetException(new TaskCanceledException("Activation was cancelled", null, cancellationToken));
            });*/

            // 启动延迟任务（不使用取消令牌）
            var delayTask = Task.Delay(TimeSpan.FromMilliseconds(delayMs), TimeProvider, CancellationToken.None);

            // 等待延迟完成，或等待取消触发异常
            var completedTask = await Task.WhenAny(delayTask, tcs.Task);

            // 如果 TCS 对应的任务已完成，则其中包含异常；对其执行 await 以传播异常
            if (completedTask == tcs.Task)
            {
                await tcs.Task;
            }
        }

        IsActivatedSuccessfully = true;
        Logger.LogDebug("OnActivateAsync completed successfully for {GrainType}", GetType().Name);
        await base.OnActivateAsync(cancellationToken);
    }
}

/// <summary>
/// 无条件抛出 ObjectDisposedException（并非由于取消）的 Grain。
/// 用于测试因其他原因抛出的 ObjectDisposedException 不会被视为取消。
/// </summary>
public class ActivationCancellation_ThrowsObjectDisposedUnconditionallyGrain
    : ActivationCancellationTestGrainBase, IActivationCancellation_ThrowsObjectDisposedUnconditionallyGrain
{
    public ActivationCancellation_ThrowsObjectDisposedUnconditionallyGrain(
        ILogger<ActivationCancellation_ThrowsObjectDisposedUnconditionallyGrain> logger,
        IGrainRuntime grainRuntime)
        : base(logger, grainRuntime)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("OnActivateAsync starting for {GrainType}", GetType().Name);

        // 检查是否应该抛出异常（未请求取消）
        if (RequestContext.Get("throw_object_disposed") is bool shouldThrow && shouldThrow)
        {
            Logger.LogDebug("Throwing ObjectDisposedException unconditionally (cancellation NOT requested)");
            // 模拟由于与取消无关的原因抛出 ObjectDisposedException 的错误，
            // 不应将这种情况视为 ActivationCancelledException
            throw new ObjectDisposedException("SomeObject", "This object was disposed for reasons unrelated to cancellation.");
        }

        IsActivatedSuccessfully = true;
        Logger.LogDebug("OnActivateAsync completed successfully for {GrainType}", GetType().Name);
        return base.OnActivateAsync(cancellationToken);
    }
}

/// <summary>
/// 无条件抛出 OperationCanceledException（并非由于取消令牌被取消）的 Grain。
/// 测试因其他原因抛出的 OperationCanceledException 不会被视为 ActivationCancelledException。
/// </summary>
public class ActivationCancellation_ThrowsOperationCancelledUnconditionallyGrain
    : ActivationCancellationTestGrainBase, IActivationCancellation_ThrowsOperationCancelledUnconditionallyGrain
{
    public ActivationCancellation_ThrowsOperationCancelledUnconditionallyGrain(
        ILogger<ActivationCancellation_ThrowsOperationCancelledUnconditionallyGrain> logger,
        IGrainRuntime grainRuntime)
        : base(logger, grainRuntime)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("OnActivateAsync starting for {GrainType}", GetType().Name);

        // 检查是否应该抛出异常（未请求取消）
        if (RequestContext.Get("throw_operation_cancelled") is bool shouldThrow && shouldThrow)
        {
            Logger.LogDebug("Throwing OperationCanceledException unconditionally (cancellation NOT requested on the passed token)");
            // 模拟由于与激活实例的取消令牌被取消无关的原因
            // 而抛出 OperationCanceledException 的场景
            // 代码应先检查 cancellationToken.IsCancellationRequested，再将异常转换为 ActivationCancelledException
            throw new OperationCanceledException("Operation was cancelled for reasons unrelated to activation cancellation.");
        }

        IsActivatedSuccessfully = true;
        Logger.LogDebug("OnActivateAsync completed successfully for {GrainType}", GetType().Name);
        return base.OnActivateAsync(cancellationToken);
    }
}
