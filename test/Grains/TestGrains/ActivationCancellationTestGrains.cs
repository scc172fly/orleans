using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains;

/// <summary>
/// 启动取消测试的基础等级 grains。
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
    /// 得到 TimeProvider 从 grain 运行时间。
    /// </summary>
    protected TimeProvider TimeProvider => _grainRuntime.TimeProvider;

    public Task<string> GetActivationId() => Task.FromResult(ActivationId);

    public Task<bool> IsActivated() => Task.FromResult(IsActivatedSuccessfully);
}

/// <summary>
/// Grain 投 OperationCanceledException 期间 OnActivateAsync 当取消标记被触发时 。
/// 此模拟代码, 通过将取消符号转成自动同步方法来正确观察此标记 。
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

        // 检查是否我们应该模拟导致取消的延迟
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
/// Grain 投 ObjectDisposedException 期间 OnActivateAsync.
/// 此模拟代码, 无法观察取消的标记, 但试图访问服务
/// 注销后已处理的。
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

            // 请求取消时重回注册以丢弃 ObjectDisposedException
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
/// Grain 中丢弃通用例外 OnActivateAsync (与取消无关)。
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

        // 检查是否应该破例
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
/// 顺利启动的Grain , 没有问题 。
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
/// Grain 投 TaskCanceledException 期间 OnActivateAsync.
/// TaskCanceledException 继承的 OperationCanceledException 并且应该以同样的方式处理
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

            // 请求取消时重回注册以丢弃 TaskCanceledException
            /*等待使用 var 登记= cancellationToken.Register(() * ______________________________________________________________________________________
            {
                Logger.LogDebug("Cancellation was requested, throwing TaskCanceledException");
                tcs.TrySetException(新的 TaskCanceledException("启动被取消"...无效, cancellationToken));
            });*/

            // 开始延迟任务( 没有取消标记)
            var delayTask = Task.Delay(TimeSpan.FromMilliseconds(delayMs), TimeProvider, CancellationToken.None);

            // 等待完成延误或取消以触发例外
            var completedTask = await Task.WhenAny(delayTask, tcs.Task);

            // 如果技术服务处的任务完成,它有一个例外,等待其推广
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
/// Grain 投 ObjectDisposedException (不是由于取消)
/// 出于其他原因投掷的 ObjectDisposedException 试验不被视为取消。
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

        // 检查我们是否应该排除例外( 不请求取消)
        if (RequestContext.Get("throw_object_disposed") is bool shouldThrow && shouldThrow)
        {
            Logger.LogDebug("Throwing ObjectDisposedException unconditionally (cancellation NOT requested)");
            // 此模拟错误, 模拟 ObjectDisposedException 被扔出理由
            // 与取消无关的注销 - 不应被视为 ActivationCancelledException
            throw new ObjectDisposedException("SomeObject", "This object was disposed for reasons unrelated to cancellation.");
        }

        IsActivatedSuccessfully = true;
        Logger.LogDebug("OnActivateAsync completed successfully for {GrainType}", GetType().Name);
        return base.OnActivateAsync(cancellationToken);
    }
}

/// <summary>
/// Grain 投 OperationCanceledException (不是因为取消象征性的取消而无条件取消)
/// 出于其他原因投掷的 OperationCanceledException 试验不作为 ActivationCancelledException处理。
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

        // 检查我们是否应该排除例外( 不请求取消)
        if (RequestContext.Get("throw_operation_cancelled") is bool shouldThrow && shouldThrow)
        {
            Logger.LogDebug("Throwing OperationCanceledException unconditionally (cancellation NOT requested on the passed token)");
            // 模拟假想 OperationCanceledException 被扔出理由
            // 取消标记
            // 代码应该检查 cancellationToken.IsCancellationRequested 转换为 ActivationCancelledException
            throw new OperationCanceledException("Operation was cancelled for reasons unrelated to activation cancellation.");
        }

        IsActivatedSuccessfully = true;
        Logger.LogDebug("OnActivateAsync completed successfully for {GrainType}", GetType().Name);
        return base.OnActivateAsync(cancellationToken);
    }
}
