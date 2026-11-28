using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Orleans.Durable.Tasks;

/// <summary>描述正在运行的持久化任务，并提供其宿主的服务。</summary>
/// <remarks>
/// <para>
/// 任务代码通过 <see cref="DurableTask.Current"/> 读取正在运行的任务上下文。宿主从此类型派生，以便
/// 调度和记录任务的各项操作。
/// </para>
/// <para>
/// 执行实例的代码在同一个调度器上运行，即宿主创建上下文时的当前调度器，例如该执行实例所在
/// Grain 激活实例的调度器，因此上下文的状态无需同步。宿主在该调度器上创建上下文、请求
/// 任务取消并取消执行尝试，执行实例的代码也在该调度器上分配标识符并运行
/// 效果：如果代码在其他位置运行操作，例如在 `ConfigureAwait(false)` 之后，则会抛出
/// <see cref="InvalidOperationException"/>。执行实例所观察的取消令牌和句柄仍可安全使用
/// ，且可从任何线程访问。
/// </para>
/// </remarks>
public abstract class DurableTaskContext
{
    private const char GeneratedSegmentPrefix = '$';
    private const string OperationInCoveredRegionMessage =
        "A DurableTask.RunInline effect, and other code whose outcome one record covers, such as a host's operation on "
        + "journaled state or a transaction's body, runs once: a replay returns the record without running the code. A "
        + "durable operation there would be missing from the replay, and the identifiers of the body's later operations "
        + "would shift. Run the operation in the body, before or after the covered code (ORLEANS0227).";

    private const string OffSchedulerMessage =
        "Durable task code ran off the scheduler which runs its execution, such as its grain activation's scheduler, for "
        + "example after ConfigureAwait(false). The execution orders and records its operations on that scheduler. Await "
        + "without ConfigureAwait(false) in durable task bodies and steps (ORLEANS0226).";

    private static readonly AsyncLocal<DurableTaskContext?> AmbientContext = new();
    private static readonly AsyncLocal<DurableTaskContext?> ShieldedContext = new();

    // 标记 RunInCoveredFlowAsync 为所指定执行实例覆盖的异步流。
    private static readonly AsyncLocal<DurableTaskContext?> CoveredFlow = new();

    private readonly TaskScheduler _scheduler = TaskScheduler.Current;

    // 在任务取消和执行尝试取消时均会取消：它是 CancellationToken 的来源。
    private readonly CancellationTokenSource _cancellationSource = new();

    // 仅在执行尝试取消时取消，并且先于 _cancellationSource 取消。因此，观察任一令牌的代码都会发现
    // 执行尝试已被取消。
    private readonly CancellationTokenSource _attemptCancellationSource = new();
    private bool _taskCancellationRequested;
    private string? _taskCancellationReason;
    private DurableTaskAttemptCancellationReason? _attemptCancellationReason;
    private long _nextChildId;
    private Dictionary<string, int>? _nameOccurrences;

    // 受记录覆盖的区域：同步区域的嵌套深度（这些区域不会执行等待操作），以及异步
    // 覆盖执行流的数量，这些执行流由 CoveredFlow 标记。
    private int _coveredDepth;
    private int _coveredFlows;
    /// <summary>初始化持久化任务 <paramref name="taskId"/> 的一次执行尝试。</summary>
    /// <param name="taskId">此次执行尝试所运行任务的稳定标识符。</param>
    /// <remarks>
    /// 宿主在运行执行实例代码的调度器上创建上下文。它通过以下方法请求任务的持久化
    /// 取消：<see cref="RequestTaskCancellation(string?)"/>，并通过以下方法停止本次执行尝试：
    /// <see cref="CancelAttempt(DurableTaskAttemptCancellationReason)"/>。宿主会等待已取消的执行尝试结束，丢弃其
    /// 响应；如果任务仍需要产生结果，则在新上下文中再次运行任务。
    /// </remarks>
    protected DurableTaskContext(TaskId taskId)
    {
        if (taskId.IsDefault)
        {
            throw new ArgumentException("A durable execution requires an explicit task identifier.", nameof(taskId));
        }

        Id = taskId;
    }

    /// <summary>获取调用代码所在的持久化任务上下文（若存在）。</summary>
    /// <param name="context">正在运行的任务上下文；若位于持久化任务之外，则为 <see langword="null"/>。</param>
    /// <returns><see langword="true"/> 表示调用代码在持久化任务中运行。</returns>
    /// <remarks>仅在持久化任务中运行的代码可直接读取 <see cref="DurableTask.Current"/>。</remarks>
    public static bool TryGetCurrent([NotNullWhen(true)] out DurableTaskContext? context)
    {
        context = AmbientContext.Value;
        return context is not null;
    }

    /// <summary>获取调用代码所在的持久化任务上下文；若不存在，则为 <see langword="null"/>。</summary>
    internal static DurableTaskContext? Ambient => AmbientContext.Value;

    /// <summary>获取此执行实例的标识符。</summary>
    public TaskId Id { get; }

    /// <summary>获取标识此执行实例所属根任务此次运行的唯一值（运行世代标识）。</summary>
    /// <remarks>
    /// <para>
    /// 宿主在首次存储被调用根任务时生成运行世代标识，并将其与该任务一起记入日志。每个
    /// 任务树中的执行实例（包括步骤和附接子任务）都报告相同的运行世代标识，而
    /// 此次运行的每次重放与执行尝试也都报告该标识。以后再次运行同一根标识符的任务时，例如
    /// 在此前运行完成且其历史已删除后启动的新运行，都会获得新的运行世代标识。
    /// </para>
    /// <para>
    /// 生命周期超过一次运行的标识符，例如消息标识符、取消与信号记录，以及
    /// 调用其他服务所用的幂等键，均包含运行世代标识，因此使用相同根标识符的后续运行
    /// 不会匹配先前运行的记录。其字符串形式为 <c>ToString("N")</c>。
    /// </para>
    /// </remarks>
    public abstract Guid Incarnation { get; }

    /// <summary>获取宿主为此执行实例提供的逻辑 UTC 时间。</summary>
    public abstract DateTimeOffset UtcNow { get; }

    /// <summary>
    /// 获取一个值，指示此次运行的任务主体是否正在重新执行先前运行
    /// 已记录的操作。此值仅用于日志记录和诊断。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 对于尚未完成的任务（例如 Grain 已经停用的情况），宿主会重新运行任务主体，
    /// 从头开始。该值从发现以下内容的运行开始时即为 <see langword="true"/>：之前的运行
    /// 已为此任务记录的操作，例如子任务、标记及已记录的效果，直到本次运行已到达每个
    /// 这类操作，或开始执行之前的运行未记录的新操作；之后该值保持为 <see langword="false"/>。再次到达
    /// 已有操作不会结束重放。首次运行期间始终报告 <see langword="false"/>。
    /// </para>
    /// <para>
    /// 仅报告一次事件的代码（例如通过以下方式记录日志）
    /// <see cref="DurableTaskLoggerExtensions.ReplaySafe(Microsoft.Extensions.Logging.ILogger)"/>，应在
    /// 任务重放期间跳过这些事件。状态变更和持久化操作不能依赖此值：如果一次运行在持久化
    /// 写入提交其工作之前就已结束，该运行会被重放，因此仅在此值为 <see langword="false"/> 时运行的代码可能
    /// 运行两次，也可能一次都不运行；基于此值进行分支的代码会在不同运行中调度不同的操作。需要
    /// 仅执行一次的状态变更应放在 <see cref="DurableTask.RunInline{TResult}(string, Func{TResult})"/> 中、某个步骤中，或
    /// 任务主体的同步结尾部分。
    /// </para>
    /// <para>不跟踪重放状态的宿主保留此默认实现，其返回值为 <see langword="false"/>。</para>
    /// </remarks>
    public virtual bool IsReplaying => false;

    /// <summary>获取一个值，指示是否已请求对任务进行持久化取消。</summary>
    /// <remarks>
    /// 任务取消是持久化的：宿主记录取消请求，并为每个执行实例记录
    /// 它首次观察到取消请求的位置。任务的每次执行尝试（包括重放）都在
    /// 同一位置观察到取消，因此任务主体每次运行都遵循相同的执行路径，观察到取消后的清理操作也会
    /// 像任务主体的其他操作一样被记录。决定持久化结果的代码（例如决定是否执行补偿）应检查此
    /// 值，例如在 <c>catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)</c> 中。
    /// 仅取消执行尝试不会改变此值，它仍为 <see langword="false"/>。
    /// </remarks>
    public bool IsTaskCancellationRequested => _taskCancellationRequested;

    /// <summary>
    /// 获取持久化取消请求携带的原因，或返回 <see langword="null"/>（当任务取消
    /// 尚未被请求，或者取消请求未携带原因时）。
    /// </summary>
    /// <remarks>
    /// 首次取消请求决定原因，后续请求不会更改它。宿主将该
    /// 原因与任务取消记录一起保留，因此重放时，会在原始运行首次观察到取消的位置
    /// 观察到取消，并读取相同的原因。原因在以下令牌被取消之前设置：
    /// <see cref="CancellationToken"/>，因此观察取消的代码，例如
    /// <see langword="catch"/> 代码块，可以读取该原因。
    /// </remarks>
    public string? TaskCancellationReason => _taskCancellationReason;

    /// <summary>
    /// 获取调用代码是否处于此执行实例的 <see cref="DurableTaskExtensions.Shielded(DurableTask)"/> 启动操作之内，
    /// 从而使其调度的子任务不受该执行实例的持久化取消影响。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 受屏蔽保护的定义会在屏蔽范围内启动，受屏蔽保护的组合器作为此执行实例一部分启动的输入也
    /// 同样如此。屏蔽保护随该启动操作的异步执行流传播，并且仅适用于此执行实例：
    /// 子任务自身的子任务仍照常受到其取消的影响。
    /// </para>
    /// <para>
    /// 宿主在调度子执行实例时、进行任何异步工作之前读取此值，并将其与子执行实例一同记录：
    /// 即使当前执行实例已经请求取消，宿主仍会创建并运行这种子执行实例，且不会将
    /// 当前执行实例的取消传播给它。管理操作中的中止及删除执行树仍会影响它。
    /// </para>
    /// </remarks>
    protected internal bool SchedulesShieldedChildren => ReferenceEquals(ShieldedContext.Value, this);

    /// <summary>开始对调用流调度的子任务应用取消屏蔽，并返回先前的屏蔽状态。</summary>
    internal DurableTaskContext? EnterShield()
    {
        var previous = ShieldedContext.Value;
        ShieldedContext.Value = this;
        return previous;
    }

    /// <summary>恢复 <see cref="EnterShield"/> 返回的先前屏蔽状态。</summary>
    internal static void RestoreShield(DurableTaskContext? previous) => ShieldedContext.Value = previous;

    /// <summary>
    /// 获取一个取消令牌，当请求任务的持久化取消或宿主取消本次
    /// 执行尝试时，该令牌将被取消。将其传递给普通任务代码发起的调用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 在这两种情况下，该令牌都会及时终止任务发起的调用，例如 HTTP 请求或信号量等待。
    /// <see cref="IsTaskCancellationRequested"/> 用于区分这两种情况：在任务取消后执行持久化清理的代码
    /// 应检查该值，并向清理操作发起的调用传递 <see cref="AttemptCancellationToken"/>，因为本属性返回的
    /// 令牌已经被取消。
    /// </para>
    /// <para>
    /// 使用 <see cref="DurableTaskExtensions.Shielded(DurableTask)"/> 启动的任务不受其所有者任务
    /// 取消的影响，因此在受屏蔽保护的任务内，只有任务自身被取消或
    /// 其执行尝试被取消时，此令牌才会被取消。
    /// </para>
    /// <para>
    /// 在此令牌上注册的回调是普通的 .NET 同步观察者，在令牌被取消时运行，并应
    /// 及时返回。
    /// </para>
    /// </remarks>
    public CancellationToken CancellationToken => _cancellationSource.Token;

    /// <summary>
    /// 获取在宿主取消本次执行尝试时被取消的令牌：此时 Grain 激活实例正在停止，或者任务
    /// 已被中止或删除。在任务取消后，将其传递给持久化清理操作发起的调用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 已取消的执行尝试中的每项持久化操作都会抛出 <see cref="DurableTaskAttemptCanceledException"/>，宿主会
    /// 丢弃此次执行尝试的响应。正在停止的激活实例上的任务会在新的执行尝试中重放，从而再次运行
    /// 清理代码；已中止或删除的任务则已有确定结果。观察到此令牌取消的代码应释放进程内
    /// 资源并返回。该代码对 Grain 日志化状态所做的更改会随 Grain 的下一次日志
    /// 写入提交。
    /// </para>
    /// <para>
    /// 任务取消不会取消此令牌，因此，在任务取消后的清理期间使用该令牌进行的调用，例如
    /// 用于补偿的 HTTP 请求，会一直运行到完成，除非执行尝试本身被取消。
    /// </para>
    /// </remarks>
    public CancellationToken AttemptCancellationToken => _attemptCancellationSource.Token;

    /// <summary>获取一个值，指示本次执行尝试是否已被宿主取消，或因重放分歧而取消。</summary>
    /// <remarks>
    /// 宿主丢弃已取消执行尝试的响应，并按照
    /// <see cref="AttemptCancellationReason"/> 所述方式处理任务：对于发生重放分歧或版本
    /// 不受支持的任务，在部署更新任务代码后重试；对于正在停止的激活实例、
    /// 已中止或已删除的任务，则结束本次执行尝试。
    /// </remarks>
    protected internal bool IsAttemptCanceled => _attemptCancellationReason is not null;

    /// <summary>获取本次执行尝试被取消的原因；运行期间则为 <see langword="null"/>。</summary>
    protected internal DurableTaskAttemptCancellationReason? AttemptCancellationReason => _attemptCancellationReason;

    /// <summary>
    /// 获取用于结束本执行实例内操作（如信号等待）的令牌：在屏蔽范围内，
    /// 只有执行尝试取消才会结束操作；否则，任务的持久化取消也会结束操作。
    /// </summary>
    /// <remarks>
    /// 屏蔽保护阻止此执行实例的持久化取消传播到受屏蔽启动操作所覆盖的各项操作，
    /// 无论作为子任务运行，还是作为执行实例的一部分运行，因此受屏蔽的等待在取消后仍会完成。
    /// </remarks>
    internal CancellationToken InlineOperationCancellationToken => SchedulesShieldedChildren ? AttemptCancellationToken : CancellationToken;

    /// <summary>
    /// 当调用代码无法运行此执行实例的持久化操作时抛出异常：<see cref="InvalidOperationException"/> 对应于
    /// 在执行实例的调度器之外运行或在 <see cref="DurableTask.RunInline{TResult}(string, Func{TResult})"/>
    /// 效果内部运行的情况；执行尝试被取消时则抛出 <see cref="DurableTaskAttemptCanceledException"/>。
    /// </summary>
    internal void ThrowIfCannotRunOperation()
    {
        ThrowIfOffScheduler();
        ThrowIfInCoveredRegion();
        if (IsAttemptCanceled)
        {
            throw CreateAttemptCanceledException(innerException: null);
        }
    }

    /// <summary>
    /// 在此执行实例的已覆盖区域内抛出 <see cref="InvalidOperationException"/>；该区域的记录覆盖
    /// 在该区域内运行的代码，因此重放时不再运行这些代码。
    /// </summary>
    internal void ThrowIfInCoveredRegion()
    {
        if (IsInCoveredRegion)
        {
            throw new InvalidOperationException(OperationInCoveredRegionMessage);
        }
    }

    /// <summary>创建已取消执行尝试中的持久化操作将抛出的异常。</summary>
    /// <param name="innerException">已取消的操作所抛出的异常（如果有）。</param>
    /// <exception cref="InvalidOperationException">执行尝试尚未被取消。</exception>
    protected internal DurableTaskAttemptCanceledException CreateAttemptCanceledException(Exception? innerException)
    {
        if (_attemptCancellationReason is not { } reason)
        {
            throw new InvalidOperationException("Only a canceled attempt throws the attempt's cancellation.");
        }

        return new(reason, AttemptCancellationToken, innerException);
    }

    /// <summary>
    /// 以 <see cref="DurableTaskAttemptCancellationReason.Diverged"/> 为原因取消本次执行尝试，因为其重放偏离了
    /// 此前执行尝试所记录的执行过程，并返回发生分歧的操作所抛出的异常。
    /// </summary>
    /// <param name="cause">描述重放分歧，例如已记录结果的类型不再符合代码预期。</param>
    /// <remarks>当部署后的代码再次与记录匹配时，宿主会重试该任务。</remarks>
    internal DurableTaskAttemptCanceledException Diverge(Exception cause)
    {
        CancelAttempt(DurableTaskAttemptCancellationReason.Diverged);
        return CreateAttemptCanceledException(cause);
    }

    /// <summary>
    /// 返回 <paramref name="operation"/>；若该操作因本次执行尝试被取消而以取消结束，则将取消转换为
    /// <see cref="DurableTaskAttemptCanceledException"/>.
    /// </summary>
    internal ValueTask<T> ObserveAttempt<T>(ValueTask<T> operation)
    {
        return operation.IsCompletedSuccessfully ? operation : ObserveAsync(operation);

        async ValueTask<T> ObserveAsync(ValueTask<T> operation)
        {
            try
            {
                return await operation;
            }
            catch (OperationCanceledException exception) when (exception is not DurableTaskAttemptCanceledException && IsAttemptCanceled)
            {
                throw CreateAttemptCanceledException(exception);
            }
        }
    }

    /// <summary>
    /// 返回 <paramref name="operation"/> 的响应；若由于本次执行尝试被取消而导致该操作结束或报告取消，
    /// 则将该取消转换为 <see cref="DurableTaskAttemptCanceledException"/>。
    /// </summary>
    internal ValueTask<DurableTaskResponse> ObserveAttempt(ValueTask<DurableTaskResponse> operation)
    {
        // ValueTask 只能被消费一次，因此只读取一次已完成操作的结果，并通过新的 ValueTask 返回它。
        if (operation.IsCompletedSuccessfully)
        {
            var response = operation.Result;
            return IsAttemptCancellation(response, out var canceled)
                ? ValueTask.FromException<DurableTaskResponse>(CreateAttemptCanceledException(canceled))
                : new(response);
        }

        return ObserveAsync(operation);

        async ValueTask<DurableTaskResponse> ObserveAsync(ValueTask<DurableTaskResponse> operation)
        {
            DurableTaskResponse response;
            try
            {
                response = await operation;
            }
            catch (OperationCanceledException exception) when (exception is not DurableTaskAttemptCanceledException && IsAttemptCanceled)
            {
                throw CreateAttemptCanceledException(exception);
            }

            return IsAttemptCancellation(response, out var canceled) ? throw CreateAttemptCanceledException(canceled) : response;
        }
    }

    /// <summary>
    /// 返回 <paramref name="response"/> 是否报告了因本次执行尝试被取消而结束操作的取消；
    /// 此时操作会将该取消作为 <see cref="DurableTaskAttemptCanceledException"/> 抛出。
    /// </summary>
    private bool IsAttemptCancellation(DurableTaskResponse response, [NotNullWhen(true)] out OperationCanceledException? canceled)
    {
        canceled = response is CanceledDurableTaskResponse { Exception: not DurableTaskAttemptCanceledException and var exception } && IsAttemptCanceled
            ? exception
            : null;
        return canceled is not null;
    }

    /// <inheritdoc cref="ObserveAttempt{T}(ValueTask{T})"/>
    internal ValueTask ObserveAttempt(ValueTask operation)
    {
        return operation.IsCompletedSuccessfully ? operation : ObserveAsync(operation);

        async ValueTask ObserveAsync(ValueTask operation)
        {
            try
            {
                await operation;
            }
            catch (OperationCanceledException exception) when (exception is not DurableTaskAttemptCanceledException && IsAttemptCanceled)
            {
                throw CreateAttemptCanceledException(exception);
            }
        }
    }

    /// <summary>
    /// 返回 <paramref name="exception"/> 是否因为调用方取消了
    /// <paramref name="cancellationToken"/> 而结束操作，而非宿主取消了执行尝试。
    /// </summary>
    internal static bool IsCallerCancellation(OperationCanceledException exception, CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested && exception is not DurableTaskAttemptCanceledException;

    /// <summary>
    /// 请求持久化取消此上下文正在运行的任务，并携带 <paramref name="reason"/>。
    /// </summary>
    /// <param name="reason">
    /// <see cref="TaskCancellationReason"/> 所报告的原因，若无原因则为 <see langword="null"/>。首次请求
    /// 决定该原因，因此宿主在重放已取消的任务时，会传入与任务取消一起记录的
    /// 原因。
    /// </param>
    /// <remarks>
    /// 宿主持久化记录请求，并记录此执行实例首次观察到请求的位置。宿主会在
    /// 执行实例到达该点时，在其调度器上调用此方法：首次运行中为请求到达时，
    /// 并在重放时于记录的位置调用，因此重放会在原始运行观察到取消的位置观察到取消。该
    /// 请求设置 <see cref="IsTaskCancellationRequested"/>，然后取消 <see cref="CancellationToken"/>，其
    /// 注册的回调会同步运行，与任何 <see cref="CancellationTokenSource"/> 的行为相同。后续请求不会改变
    /// 任何内容。
    /// </remarks>
    protected internal void RequestTaskCancellation(string? reason)
    {
        AssertOnScheduler();
        if (_taskCancellationRequested)
        {
            return;
        }

        _taskCancellationReason = reason;
        _taskCancellationRequested = true;
        _cancellationSource.Cancel();
    }

    /// <summary>以 <paramref name="reason"/> 为原因取消本次执行尝试。</summary>
    /// <param name="reason">取消执行尝试的原因。</param>
    /// <remarks>
    /// 宿主在执行实例的调度器上调用此方法。取消会设置 <see cref="AttemptCancellationReason"/>，然后
    /// 依次取消 <see cref="AttemptCancellationToken"/> 和 <see cref="CancellationToken"/>，其注册的回调会
    /// 同步运行。从此以后，执行尝试中的持久化操作会抛出
    /// <see cref="DurableTaskAttemptCanceledException"/>，携带首次调用指定的原因；后续调用不会更改任何内容。
    /// 宿主等待本次执行尝试结束并丢弃其响应。
    /// </remarks>
    protected internal void CancelAttempt(DurableTaskAttemptCancellationReason reason)
    {
        AssertOnScheduler();
        if (_attemptCancellationReason is not null)
        {
            return;
        }

        _attemptCancellationReason = reason;
        try
        {
            _attemptCancellationSource.Cancel();
        }
        finally
        {
            _cancellationSource.Cancel();
        }
    }

    /// <summary>断言宿主在运行此执行实例且拥有其状态的调度器上调用此方法。</summary>
    private void AssertOnScheduler()
        => Debug.Assert(
            TaskScheduler.Current == _scheduler,
            "A durable execution's state is confined to the scheduler which was current when its host created its context.");

    /// <summary>
    /// 任务代码在运行此执行实例的调度器之外运行时抛出 <see cref="InvalidOperationException"/>，
    /// 因为在其他调度器上无法对其操作进行排序或记录。
    /// </summary>
    private void ThrowIfOffScheduler()
    {
        if (TaskScheduler.Current != _scheduler)
        {
            throw new InvalidOperationException(OffSchedulerMessage);
        }
    }

    /// <summary>
    /// 启动或重新附接到 <paramref name="taskDefinition"/>（使用标识符 <paramref name="taskId"/>），并将启动记录
    /// 保存到此执行实例中。
    /// </summary>
    /// <param name="taskId">
    /// 对于附接任务，是 <see cref="Id"/> 的子标识符；对于
    /// <see cref="DurableTaskStartOptions.Detached"/> 任务，则是根标识符。
    /// </param>
    /// <param name="taskDefinition">要启动的任务定义。</param>
    /// <param name="options">任务与此执行实例的关系，以及启动操作何时完成。</param>
    /// <param name="cancellationToken">用于放弃启动操作的取消令牌。它不会取消任务本身。</param>
    /// <returns>供此执行实例观察任务的句柄。</returns>
    /// <remarks>
    /// <para>
    /// 宿主将启动记录保存到此执行实例的树中，并与任务的启动一起记录，因此重放到达
    /// 该记录时会观察同一个任务，而不会启动另一个任务。附接任务是子任务
    /// <paramref name="taskId"/>，由此执行实例拥有；其取消会传播到子任务，除非启动时
    /// 受到屏蔽保护（由 <see cref="SchedulesShieldedChildren"/> 报告）。分离任务是独立的根任务，不受此执行实例的
    /// 取消、中止或删除影响。宿主在分离任务的
    /// 启动记录中将此执行实例指定为订阅者，因此返回的句柄能够接收任务结果；取消句柄或此
    /// 执行实例只会结束该订阅。
    /// </para>
    /// <para>
    /// 当在此执行实例已经记录的标识符下启动任务且定义表示同一个请求时，会重新附接到已记录的任务，
    /// 并返回其句柄。如果在已记录的标识符下发起不同请求，则发生重放分歧：
    /// 宿主以 <see cref="DurableTaskAttemptCancellationReason.Diverged"/> 为原因取消执行尝试，不会启动该任务。
    /// </para>
    /// <para>
    /// 返回的操作会在启动记录持久化后完成。使用
    /// <see cref="DurableTaskStartOptions.WaitForAcceptance"/> 时，还需要等到任务宿主已接受并
    /// 持久化存储该任务，或者任务结果已经明确，才会完成；这样任务标识符便可交给其他参与方
    /// 用于寻址该任务。
    /// </para>
    /// </remarks>
    protected internal abstract ValueTask<IScheduledTaskHandle> StartTaskAsync(
        TaskId taskId,
        DurableTask taskDefinition,
        DurableTaskStartOptions options,
        CancellationToken cancellationToken);

    /// <summary>
    /// 暂存 <paramref name="taskDefinition"/> 的启动操作（使用标识符 <paramref name="taskId"/>），并将该操作记录在此
    /// 执行实例中；上述过程同步进行。
    /// </summary>
    /// <param name="taskId">
    /// 对于附接任务，是 <see cref="Id"/> 的子标识符；对于
    /// <see cref="DurableTaskStartOptions.Detached"/> 任务，则是根标识符。
    /// </param>
    /// <param name="taskDefinition">要启动的任务定义。</param>
    /// <param name="options"><see cref="DurableTaskStartOptions.None"/> 或 <see cref="DurableTaskStartOptions.Detached"/>。</param>
    /// <returns>供此执行实例观察任务的句柄。</returns>
    /// <remarks>
    /// 宿主在调用代码所在的执行轮次中暂存启动操作及其记录，两者会随宿主的下一次持久化
    /// 写入一起提交，同时提交该轮次所做的状态变更。重放到达记录时会返回同一个
    /// 句柄，而不会再次启动任务。各选项所指定的任务与此执行实例的关系如
    /// <see cref="StartTaskAsync"/> 所述。无法同步暂存启动操作的宿主保留此默认
    /// 实现，该实现抛出 <see cref="NotSupportedException"/>。
    /// </remarks>
    protected internal virtual IScheduledTaskHandle PostTask(
        TaskId taskId,
        DurableTask taskDefinition,
        DurableTaskStartOptions options)
        => throw new NotSupportedException("The durable execution host does not support posting tasks.");

    /// <summary>调度在 <paramref name="dueTime"/> 到期的持久化延迟。</summary>
    protected internal abstract ValueTask<DurableTaskResponse> ScheduleDelayAsync(
        TaskId taskId,
        DateTimeOffset dueTime,
        CancellationToken cancellationToken);

    /// <summary>
    /// 当此执行实例的任务体完成时接收其终态响应，与任务体的最后部分代码同步执行。
    /// </summary>
    /// <param name="response">任务主体的终态响应。</param>
    /// <remarks>
    /// <para>
    /// 运行任务主体的定义会在主体的任何延续任务运行之前调用此方法：<see langword="async"/>
    /// 持久化任务方法从完成其状态机的步骤中调用此方法；委托定义则
    /// 从完成委托的工作项中调用，或者从与该工作项同步运行的延续任务中调用。
    /// </para>
    /// <para>
    /// 如果宿主将任务主体所做的状态变更（例如 Grain 的日志化
    /// 状态）与主体结果一起提交，则会在此处暂存结果。在主体的最后一段代码之后运行的工作，例如由另一
    /// 操作请求的提交，会将此结果与上述代码所做的变更一起提交。宿主仍然通过
    /// <see cref="DurableTask.RunAsync(DurableTaskContext)"/> 接收响应。实现必须正常返回，不得
    /// 抛出异常。默认实现不执行任何操作。
    /// </para>
    /// </remarks>
    protected internal virtual void OnBodyCompleted(DurableTaskResponse response)
    {
    }

    /// <summary>
    /// 等待包含此执行实例的被调用任务中名为 <paramref name="name"/> 的信号，
    /// 并返回携带该信号已记录值的成功响应。
    /// </summary>
    /// <remarks>
    /// 宿主记录收到的被调用任务每个名称对应的第一个信号值，并与任务状态一起保存，
    /// 将其返回给等待该名称的每个操作，包括重放期间的等待。如果信号早于等待到达，
    /// 则立即完成等待。取消 <paramref name="cancellationToken"/> 会放弃等待。不支持
    /// 信号的宿主保留此默认实现，该实现抛出 <see cref="NotSupportedException"/>。
    /// </remarks>
    protected internal virtual ValueTask<DurableTaskResponse> WaitForSignalAsync(
        string name,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("The durable execution host does not support signals.");

    /// <summary>
    /// 返回一个已完成的候选任务；对于具有相同
    /// <paramref name="decisionId"/> 的每次调用（包括重放期间的调用），都返回同一个候选任务。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 选中的候选任务是最先完成的任务：宿主在 <paramref name="decisionId"/> 下持久化选择结果，
    /// 或依据持久化完成顺序推导出该结果，例如宿主暂存每个终态结果时赋予的序号。
    /// 这种持久化顺序无需额外记录选择结果：每次重放时，已完成候选任务中序号最小的那个都相同，
    /// 因为之后完成的候选任务会获得更大的序号。
    /// </para>
    /// <para>
    /// <see cref="DurableTask.WhenAny(IReadOnlyList{DurableTask})"/> 通过一次调用选择最先完成的任务，而
    /// <see cref="ScheduledTask.WhenEach(IReadOnlyList{ScheduledTask})"/> 每次调用都在
    /// 剩余候选任务中选择下一个任务，且使用不同的决策标识符。宿主读取 <paramref name="candidates"/> 的时机仅限于
    /// 本次调用期间。
    /// </para>
    /// <para>
    /// 即使代码发生变更，导致重放时的候选任务不再包含先前记录的选择结果，宿主仍会返回该结果。
    /// 随后调用操作会以如下原因取消执行尝试：
    /// <see cref="DurableTaskAttemptCancellationReason.Diverged"/>，因此任务保持待处理状态，直到与
    /// 记录匹配的代码再次重放该任务。
    /// </para>
    /// </remarks>
    protected internal abstract ValueTask<TaskId> SelectCompletionAsync(
        TaskId decisionId,
        IReadOnlyList<TaskId> candidates,
        CancellationToken cancellationToken);

    /// <summary>
    /// 创建此执行实例下一个操作的标识符；如果提供了 <paramref name="name"/>，则使用该名称。
    /// </summary>
    /// <remarks>
    /// 同一次运行中重复使用的名称按出现顺序标识后续各次操作：第一次使用 <paramref name="name"/>，
    /// 后续出现会追加序号，例如 <c>reserve$2</c>，因此循环中命名的子任务能够在
    /// 重放时保持标识不变。命名子任务、命名组合器和 <see cref="DurableTask.RunInline{TResult}(string, Func{TResult})"/>
    /// 效果按名称共享计数。名称不能包含生成标识符所使用的 <c>$</c>。无名称的子任务
    /// 与无名称的 <see cref="DurableTask.RunInline{TResult}(Func{TResult})"/> 效果共享生成标识符的计数，
    /// 例如 <c>$child-1</c>、<c>$child-2</c> 等。当无名称的
    /// 操作以相同的确定性顺序创建时，生成的标识符在重放中保持稳定。并发批次可使用
    /// <see cref="DurableTask.WhenAll(IReadOnlyList{DurableTask})"/> 和
    /// <see cref="DurableTask.WhenAny(IReadOnlyList{DurableTask})"/>，其输入索引提供稳定的逻辑位置。
    /// 独立的并发调度操作（包括并发的组合器调用）应使用显式名称，或
    /// 以其他方式确保按确定性顺序启动。
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为空或包含 <c>$</c>。</exception>
    /// <exception cref="InvalidOperationException">调用代码正在效果中运行，或在执行实例的调度器之外运行。</exception>
    internal TaskId CreateChildTaskId(string? name)
    {
        if (name is not null)
        {
            ValidateName(name, nameof(name));
            return CreateNamedChildId(name);
        }

        ThrowIfOffScheduler();
        ThrowIfInCoveredRegion();
        return Id.Child($"$child-{(++_nextChildId).ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// 获取运行此执行实例的宿主标识（例如其 Grain），用于限定
    /// 此执行实例为所启动的分离任务派生的根标识符；若无宿主标识则为 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// 任务标识符在其宿主内唯一，因此两个宿主上的执行实例可以拥有相同标识符。若宿主上的执行实例
    /// 在其他宿主也会启动任务的目标上启动分离任务，则此宿主应提供自身标识，使各执行实例
    /// 所派生的根标识符互不相同。
    /// </remarks>
    protected internal virtual string? HostId => null;

    /// <summary>
    /// 为此执行实例作为自身操作启动的分离任务创建根标识符，操作为
    /// <paramref name="operationId"/>.
    /// </summary>
    /// <remarks>
    /// 根标识符是一个单独的段，其中包含 <see cref="HostId"/> 的转义路径（如果宿主具有该标识），
    /// 后接 <paramref name="operationId"/> 的各个段：<c>{HostId}/{Id}/{segment}</c>，其中每个部分
    /// 都对其中的 <c>/</c> 和 <c>\</c> 进行转义，方式与 <see cref="TaskId"/> 相同。因此，不同宿主、执行实例及操作
    /// 派生不同的根标识符；重放到达同一操作时会派生出同一根标识符，其他参与方可根据
    /// 宿主标识、执行实例标识符及操作的段来派生该根标识符。
    /// </remarks>
    internal TaskId CreateDetachedRootId(TaskId operationId)
    {
        Debug.Assert(operationId.IsChildOf(Id), "A detached task's operation is a child of the starting execution.");
        var path = operationId.ToString();
        return TaskId.CreateRoot(HostId is { } hostId ? $"{TaskId.CreateRoot(hostId)}/{path}" : path);
    }

    /// <summary>创建类型为 <paramref name="kind"/> 的无名称组合器或选择操作的标识符。</summary>
    /// <remarks>
    /// 每种类型都独立统计其操作，例如 <c>$when-any-1</c> 和 <c>$when-any-2</c>，因此一种类型的操作数量
    /// 发生变化时，不会改变另一种类型的标识符。
    /// </remarks>
    internal TaskId CreateOperationId(string kind) => Id.Child($"${kind}-{NextOccurrence(GeneratedSegmentPrefix + kind).ToString(CultureInfo.InvariantCulture)}");

    /// <summary>
    /// 创建类型为 <paramref name="kind"/> 且观察 <paramref name="subject"/> 的操作标识符，
    /// 例如子任务等待的超时：<c>{segment}${kind}-{n}</c>，其中 <c>{segment}</c> 是操作对象的路径段
    /// 其中 <c>{n}</c> 表示针对该对象的此类操作的计数。
    /// </summary>
    /// <remarks>
    /// 该标识符仅取决于观察对象及此前针对该对象的同类操作，因此针对其他
    /// 对象执行的操作不会改变该标识符。若操作对象不是此执行实例的子任务，则取该类型的下一个标识符。
    /// </remarks>
    internal TaskId CreateOperationId(string kind, TaskId subject)
    {
        if (!subject.IsChildOf(Id))
        {
            return CreateOperationId(kind);
        }

        var name = $"{subject.LastSegment}{GeneratedSegmentPrefix}{kind}";
        return Id.Child($"{name}-{NextOccurrence(name).ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>统计本次执行尝试中 <paramref name="key"/> 的一次出现，并返回其从 1 开始的序号。</summary>
    private int NextOccurrence(string key)
    {
        ThrowIfOffScheduler();
        ThrowIfInCoveredRegion();
        _nameOccurrences ??= new(StringComparer.Ordinal);
        _nameOccurrences.TryGetValue(key, out var occurrence);
        _nameOccurrences[key] = ++occurrence;
        return occurrence;
    }

    /// <summary>验证调用方提供的名称；名称不能包含生成标识符所使用的 <c>$</c>。</summary>
    internal static void ValidateName(string name, string paramName)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, paramName);
        if (name.Contains(GeneratedSegmentPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Names can't contain '{GeneratedSegmentPrefix}', which generated identifiers use.",
                paramName);
        }
    }

    /// <summary>
    /// 获取调用代码是否正在此执行实例的记录覆盖区域内运行：该区域代码的结果由执行实例中的一条
    /// 记录覆盖，因此重放时返回记录而不再运行代码。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 覆盖区域是使已被记录覆盖的代码不进入执行历史的一种机制：
    /// 例如同步运行的 <see cref="DurableTask.RunInline{TResult}(string, Func{TResult})"/> 效果，以及宿主的
    /// 覆盖代码，如日志状态上的操作及其运行的回调，或
    /// 由一条记录跨越其各等待点覆盖其结果的异步任务体（例如事务的任务体）。
    /// </para>
    /// <para>
    /// 在区域内，嵌套的效果作为该区域的一部分直接运行，不具有自己的标识符或
    /// 独立记录；其他所有持久化操作（例如启动、延迟、信号等待、组合器或版本检查）
    /// 都会在取得标识符之前抛出 <see cref="InvalidOperationException"/>。宿主不会将该区域的
    /// 对状态的访问记录为执行实例自身的访问。
    /// </para>
    /// </remarks>
    internal bool IsInCoveredRegion => _coveredDepth > 0 || (_coveredFlows > 0 && ReferenceEquals(CoveredFlow.Value, this));

    /// <summary>
    /// 进入此执行实例的同步记录覆盖区域，该区域在返回的值被释放时结束，且必须在执行实例调度器上的同一段
    /// 同步代码执行期间结束。
    /// </summary>
    internal CoveredRegion EnterCoveredRegion()
    {
        AssertOnScheduler();
        _coveredDepth++;
        return new(this);
    }

    /// <summary>在同步记录覆盖区域内运行效果的委托。</summary>
    internal TResult RunAsEffect<TState, TResult>(TState state, Func<TState, TResult> body)
    {
        using var region = EnterCoveredRegion();
        return body(state);
    }

    /// <summary>
    /// 将异步 <paramref name="body"/> 的整个执行流作为此执行实例的一个记录覆盖区域运行，包括
    /// 其等待操作的延续任务，直至其任务完成。
    /// </summary>
    /// <remarks>
    /// 任务体在执行实例的调度器上启动，并在任务体完成后此方法恢复到该调度器时结束区域，
    /// 此主体没有启动的执行实例中的其他执行流不在覆盖范围内。
    /// </remarks>
    internal async Task<TResult> RunInCoveredFlowAsync<TResult>(Func<Task<TResult>> body)
    {
        AssertOnScheduler();
        _coveredFlows++;
        try
        {
            return await StartCovered(body);
        }
        finally
        {
            _coveredFlows--;
        }

        // 标记随任务主体的异步执行流传播；主体返回后，此方法的执行流会恢复先前的标记。
        Task<TResult> StartCovered(Func<Task<TResult>> body)
        {
            var previous = CoveredFlow.Value;
            CoveredFlow.Value = this;
            try
            {
                return body();
            }
            finally
            {
                CoveredFlow.Value = previous;
            }
        }
    }

    /// <summary>结束由 <see cref="EnterCoveredRegion"/> 进入的同步记录覆盖区域。</summary>
    internal readonly struct CoveredRegion(DurableTaskContext context) : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose() => context._coveredDepth--;
    }

    /// <summary>
    /// 创建此执行实例中有名称的操作的标识符：<paramref name="name"/> 在本次执行尝试中
    /// 第一次出现时使用 <c>{name}</c>，之后每次出现都追加序号，例如 <c>reserve$2</c>。
    /// </summary>
    /// <param name="name">调用方提供的名称。</param>
    /// <remarks>
    /// 各次出现按执行尝试创建它们的顺序编号，因此重放时以相同顺序创建同名
    /// 操作会分配相同的标识符，并且添加后续操作不会改变先前操作的名称。
    /// 子任务、组合器和效果按名称共享计数，因此同一名称的一次出现具有相同的
    /// 标识符，无论哪种操作使用它。如果宿主发现某个标识符下记录的是另一种操作，
    /// 则报告重放分歧，而不是运行该操作。
    /// </remarks>
    internal TaskId CreateNamedChildId(string name)
    {
        var occurrence = NextOccurrence(name);
        return Id.Child(occurrence == 1
            ? name
            : $"{name}{GeneratedSegmentPrefix}{occurrence.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// 返回效果 <paramref name="effectId"/> 已记录的结果，或者运行该效果并记录其结果。
    /// </summary>
    /// <typeparam name="TState"><paramref name="effect"/> 接收的状态的类型。</typeparam>
    /// <param name="effectId">效果的标识符，是此执行实例的子标识符。</param>
    /// <param name="state"><paramref name="effect"/> 接收的状态。</param>
    /// <param name="effect">
    /// 运行效果并返回其结果：成功结果或其抛出的异常。该函数自身不会抛出异常。
    /// </param>
    /// <returns>宿主记录的效果结果。</returns>
    /// <remarks>
    /// <para>
    /// <see cref="DurableTask.RunInline{TResult}(string, Func{TResult})"/> 从任务主体同步调用此方法。宿主
    /// 在同一同步工作项内运行效果并暂存其记录，因此记录会随下一次持久化
    /// 写入时会与效果所做的状态变更一起提交。任务主体的每次后续运行（包括重放）
    /// 都会接收已记录的结果，而不再运行该效果。
    /// </para>
    /// <para>
    /// 如果宿主无法存储结果，例如结果无法序列化，则会转而记录并返回该
    /// 失败，使每次运行都观察到同样的失败。如果宿主无法将记录与效果的状态变更一起暂存，则会抛出 <see cref="InvalidOperationException"/>，
    /// 而不会运行效果，例如此方法在
    /// 宿主调度器之外被调用时。如果先前运行在效果的标识符下记录了另一种操作（例如步骤），
    /// 则重放偏离了已记录的执行过程：宿主会以下列原因为由取消执行尝试：
    /// <see cref="DurableTaskAttemptCancellationReason.Diverged"/>，而不运行效果，并使任务保持待处理状态。
    /// 不支持记录效果的宿主保留此默认实现，该实现抛出
    /// <see cref="NotSupportedException"/>.
    /// </para>
    /// </remarks>
    protected internal virtual DurableTaskResponse RunEffect<TState>(
        TaskId effectId,
        TState state,
        Func<TState, DurableTaskResponse> effect)
        => throw new NotSupportedException("The durable task host does not support DurableTask.RunInline.");

    /// <summary>
    /// 返回当前执行实例应遵循的变更 <paramref name="name"/> 的版本，并在
    /// 当前执行实例首次使用该名称调用时确定并记录版本。
    /// </summary>
    /// <param name="name">已验证的变更名称。</param>
    /// <param name="minSupported">调用代码支持的最早版本，至少为 <c>0</c>。</param>
    /// <param name="maxSupported">最新版本，不小于 <paramref name="minSupported"/>。</param>
    /// <returns>此执行实例的版本，范围为 <paramref name="minSupported"/> 至 <paramref name="maxSupported"/>。</returns>
    /// <remarks>
    /// <para>
    /// <see cref="DurableTask.GetVersion(string, int, int)"/> 从任务主体同步调用此方法。宿主
    /// 返回此执行实例或其外围执行实例已记录的版本；否则作出如下决定：
    /// 若先前的执行尝试记录了此次执行尝试尚未到达的操作，则使用 <c>0</c>；否则
    /// 采用 <paramref name="maxSupported"/>，并将该决策与执行实例一起记录。
    /// </para>
    /// <para>
    /// 当执行实例的版本超出支持范围时，宿主以 <see cref="DurableTaskAttemptCancellationReason.UnsupportedVersion"/> 为原因取消
    /// 执行尝试，并抛出
    /// <see cref="DurableTaskAttemptCanceledException"/>，其内部异常为
    /// <see cref="DurableTaskVersionException"/>；任务保持待处理状态。不支持版本控制的宿主保留此
    /// 默认实现，该实现抛出 <see cref="NotSupportedException"/>。
    /// </para>
    /// </remarks>
    protected internal virtual int GetVersion(string name, int minSupported, int maxSupported)
        => throw new NotSupportedException("The durable task host does not support DurableTask.GetVersion.");

    /// <summary>返回当前执行尝试所遵循的变更 <paramref name="name"/> 的版本。</summary>
    internal int DecideVersion(string name, int minSupported, int maxSupported)
    {
        ThrowIfCannotRunOperation();
        return GetVersion(name, minSupported, maxSupported);
    }

    internal static ContextScope Enter(DurableTaskContext context)
    {
        var previous = AmbientContext.Value;
        AmbientContext.Value = context;
        return new(previous);
    }

    internal readonly struct ContextScope(DurableTaskContext? previous) : IDisposable
    {
        public void Dispose() => AmbientContext.Value = previous;
    }
}
