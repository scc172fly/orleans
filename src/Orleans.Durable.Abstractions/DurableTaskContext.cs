using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Orleans.Durable.Tasks;

/// <summary>描述一项持续进行的任务,提供东道主的服务。</summary>
/// <remarks>
/// <para>
/// 任务代码从 <see cref="DurableTask.Current"/>读取运行中任务的上下文。主机由此类型产生到
/// 并记录任务操作。
/// </para>
/// <para>
/// 执行的代码运行在一个调度器上, 当主机创建上下文时该程序即为当前, 例如它
/// grain 激活的调度器, 因此上下文状态不需要同步。 主机创建了上下文、 请求
/// 任务任务取消, 并取消该调度器的尝试, 而执行的代码分配标识符和运行
/// 效果:代码在别处运行的操作,例如 `ConfigureAwait(false)`之后,抛掷
/// <see cref="InvalidOperationException"/>.吊销牌和行刑所观察到的手柄仍然安全
/// 以用于任何线索。
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

    // 标记 RunInCoveredFlowAsync 所覆盖的、用于执行其命名的行刑的无同步流。
    private static readonly AsyncLocal<DurableTaskContext?> CoveredFlow = new();

    private readonly TaskScheduler _scheduler = TaskScheduler.Current;

    // 取消后,任务取消和尝试取消: CancellationToken的来源。
    private readonly CancellationTokenSource _cancellationSource = new();

    // 仅因尝试取消而取消,在_ccanation Presourcrun前被取消。
    // 尝试取消 。
    private readonly CancellationTokenSource _attemptCancellationSource = new();
    private bool _taskCancellationRequested;
    private string? _taskCancellationReason;
    private DurableTaskAttemptCancellationReason? _attemptCancellationReason;
    private long _nextChildId;
    private Dictionary<string, int>? _nameOccurrences;

    // 覆盖区域:同步区域的深度(这些区域运行时没有等待)和无同步区域的数目
    // 流经的流量,其流量 CoveredFlow 标记。
    private int _coveredDepth;
    private int _coveredFlows;
    /// <summary>启动一项持久任务 <paramref name="taskId"/>.</summary>
    /// <param name="taskId">尝试运行任务的稳定标识。</param>
    /// <remarks>
    /// 执行执行代码的调度器上,主机创建了运行此任务代码的调度器上的背景。 它要求任务可耐久
    /// 以 <see cref="RequestTaskCancellation(string?)"/>取消,并停止此尝试
    /// <see cref="CancelAttempt(DurableTaskAttemptCancellationReason)"/>. 东道主排干取消的尝试,丢弃了
    /// 并再次在新的背景下执行这项任务,而任务仍然需要结果。
    /// </remarks>
    protected DurableTaskContext(TaskId taskId)
    {
        if (taskId.IsDefault)
        {
            throw new ArgumentException("A durable execution requires an explicit task identifier.", nameof(taskId));
        }

        Id = taskId;
    }

    /// <summary>获得呼叫代码(如果有的话)所运行的持久任务的背景。</summary>
    /// <param name="context">运行中任务上下文, 或 <see langword="null"/> 一项长期任务。</param>
    /// <returns><see langword="true"/> 当召唤代码运行在一个持久的任务。</returns>
    /// <remarks>仅用于持久任务守则: <see cref="DurableTask.Current"/>.</remarks>
    public static bool TryGetCurrent([NotNullWhen(true)] out DurableTaskContext? context)
    {
        context = AmbientContext.Value;
        return context is not null;
    }

    /// <summary>获取调用代码所运行的持久任务的背景,或 <see langword="null"/>。</summary>
    internal static DurableTaskContext? Ambient => AmbientContext.Value;

    /// <summary>获取此执行的标识符。</summary>
    public TaskId Id { get; }

    /// <summary>获取一个未识别此执行属于的根任务运行的无。</summary>
    /// <remarks>
    /// <para>
    /// 东道主在首次储存所援引的根任务时,将化身化为薄荷,并用它来做该任务的日记。
    /// 包括脚步和附属儿童,报告同样的化形,以及每个
    /// 重放并尝试运行运行时也报告它。 稍后的同一根标识符, 例如在
    /// 早期运行完成,历史被删除, 收到一个新的化身。
    /// </para>
    /// <para>
    /// 超过一次运行的识别符号,例如电文标识、取消和信号记录以及
    /// 呼叫其他服务单位的“有效”关键功能,包括化成成形,因此以后将同一根标识符转出
    /// 与前一次运行的记录不符。 它的字符串表是 <c>ToString("N")</c>。
    /// </para>
    /// </remarks>
    public abstract Guid Incarnation { get; }

    /// <summary>获得由主机提供的逻辑 UTC 执行该执行的时间。</summary>
    public abstract DateTimeOffset UtcNow { get; }

    /// <summary>
    /// 获得一个值,以显示任务身体的此运行是否是重运行操作。
    /// 记录下来,仅用于记录和诊断。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 主机重播放一个任务,但尚未完成,例如它 grain 被停止使用后,又再次运行了它的体
    /// 从开始处开始。值是 <see langword="true"/> ,从运行开始时发现较早操作的运行开始
    /// 记录此任务运行的运行量, 如儿童、 标记和记录效果等, 直到运行达到每个
    /// 或已启动一项操作,但以前没有记录过,然后停留在 <see langword="false"/>。
    /// 操作再次运行不会结束重播。整个操作中第一次运行报告 <see langword="false"/> 。
    /// </para>
    /// <para>
    /// 代码报告事件一次,例如伐木
    /// <see cref="DurableTaskLoggerExtensions.ReplaySafe(Microsoft.Extensions.Logging.ILogger)"/>,跳过它们
    /// 任务重放。 国家变化和持久操作不取决于值: 在耐久之前结束的运行
    /// 写入已承诺的工程, 将重播放它的工作, 所以代码只有在数值为 <see langword="false"/> 时运行, 代码才会运行
    /// 或两次或两次,或一次或两次,以及代码,其中的分支会以不同的运行方式安排不同操作。
    /// 运行时属于 <see cref="DurableTask.RunInline{TResult}(string, Func{TResult})"/>,在一个步骤或
    /// 身体同步尾巴
    /// </para>
    /// <para>不跟踪重播的主机保留默认的安装, 返回 <see langword="false"/>。</para>
    /// </remarks>
    public virtual bool IsReplaying => false;

    /// <summary>获得一个数值,表明是否请求持久取消任务。</summary>
    /// <remarks>
    /// 任务取消是持久的:东道主记录请求,记录每项执行,即
    /// 包括重放在内的每一项任务都观察着取消
    /// 同一点, 所以身体在每次运行中都走同样的路径, 并记录观察后进行的清理
    /// 确定持久结果的守则,例如是否赔偿,检查
    /// 数值,例如 <c>catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)</c>.
    /// 取消未遂,则 <see langword="false"/>。
    /// </remarks>
    public bool IsTaskCancellationRequested => _taskCancellationRequested;

    /// <summary>
    /// 获取持久取消请求随行的理由,或任务取消时的 <see langword="null"/>
    /// 也没有人要求,或者它的要求没有理由。
    /// </summary>
    /// <remarks>
    /// 第一次取消请求决定了原因,后来的请求保持不变。
    /// 重放, 重放时在原始
    /// 运行前先观察它,读相同的原因。原因被设置在前面
    /// <see cref="CancellationToken"/> 已被取消, 所以显示此取消的代码, 例如
    /// 块<see langword="catch"/> 读出来
    /// </remarks>
    public string? TaskCancellationReason => _taskCancellationReason;

    /// <summary>
    /// 获取调用代码是否在 <see cref="DurableTaskExtensions.Shielded(DurableTask)"/> 开始时运行
    /// 执行,这样它所计划的儿童 就不会因为这个处决的持久取消而到达
    /// </summary>
    /// <remarks>
    /// <para>
    /// 屏蔽组合器作为
    /// 执行。盾牌跟随起点的无同步流,仅适用于此执行:a
    /// 取消儿童合同时,孩子自己的子女会像往常一样得到。
    /// </para>
    /// <para>
    /// 当主机在安排孩子工作前, 与孩子一起记录下:
    /// 即使在要求取消死刑之后, 并且不传播
    /// 自动中止并删除执行的树, 仍然可以达到它。
    /// </para>
    /// </remarks>
    protected internal bool SchedulesShieldedChildren => ReferenceEquals(ShieldedContext.Value, this);

    /// <summary>开始保护呼唤流动时间表的儿童 并归还以前的盾牌</summary>
    internal DurableTaskContext? EnterShield()
    {
        var previous = ShieldedContext.Value;
        ShieldedContext.Value = this;
        return previous;
    }

    /// <summary>恢复 <see cref="EnterShield"/> 返回的盾牌。</summary>
    internal static void RestoreShield(DurableTaskContext? previous) => ShieldedContext.Value = previous;

    /// <summary>
    /// 获取当请求永久取消任务或主机取消此任务时取消的符号
    /// 。通过普通任务代码发出的电话传递到该命令。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 任务电话,如HTTP请求或等待血肿等,在这两种情况下都迅速结束。
    /// <see cref="IsTaskCancellationRequested"/> 区分它们: 任务取消后运行耐久清理的代码
    /// 检查和通过 <see cref="AttemptCancellationToken"/> 拨打清理工作所发出的电话,因为
    /// 已经取消。
    /// </para>
    /// <para>
    /// 任务始于 <see cref="DurableTaskExtensions.Shielded(DurableTask)"/> , 其主人的任务无法完成
    /// 取消, 所以在屏蔽的任务中, 此标记仅因任务本身的取消和
    /// 取消其企图。
    /// </para>
    /// <para>
    /// 此牌牌上注册的都是普通同步 .NET 观察者,当该牌子取消并返回时,这些观察者即运行
    /// 尽快
    /// </para>
    /// </remarks>
    public CancellationToken CancellationToken => _cancellationSource.Token;

    /// <summary>
    /// 获取当主机取消此尝试时取消的标记: 其 grain 激活停止, 或者任务停止
    /// 被中止或删除。 传递给任务取消后持久清理产生的通话 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 一次取消的尝试每次持久操作都会抛出 <see cref="DurableTaskAttemptCanceledException"/>,东道主也会投下
    /// 将尝试的响应丢弃。 停止激活的任务重放在新的尝试中, 运行清理
    /// 再次; 中止或删除的任务已经有结果。 遵守此符号的代码在程序中释放
    /// 和返回。它给 grain日记状态与 grain下一份日记承诺所作的更改
    /// 写。
    /// </para>
    /// <para>
    /// 任务取消时, 任务取消时会发出一个电话, 例如 任务取消清理
    /// 补偿 HTTP 请求, 运行到完成, 除非尝试本身被取消 。
    /// </para>
    /// </remarks>
    public CancellationToken AttemptCancellationToken => _attemptCancellationSource.Token;

    /// <summary>获得一个数值,显示是否由主机或重播的偏差取消了这一尝试。</summary>
    /// <remarks>
    /// 将任务作为
    /// <see cref="AttemptCancellationReason"/> 描述: 它重写重放不同或版本不同的任务
    /// 任务代码在部署修改后, 任务代码被修改, 任务停止激活的尝试终止
    /// 中止任务, 或者删除任务 。
    /// </remarks>
    protected internal bool IsAttemptCanceled => _attemptCancellationReason is not null;

    /// <summary>知道为什么这次尝试被取消了,或者 <see langword="null"/> 运行时。</summary>
    protected internal DurableTaskAttemptCancellationReason? AttemptCancellationReason => _attemptCancellationReason;

    /// <summary>
    /// 获得结束作为执行一部分的操作的标记,例如信号等待:在屏蔽内
    /// 取消企图就是为了结束它,否则任务的长期取消也是为了结束。
    /// </summary>
    /// <remarks>
    /// 屏蔽使执行的持久取消 无法到达被屏蔽开始覆盖的行动
    /// 无论是在童年时还是作为处决的一部分,所以一个屏蔽的等待在取消后完成。
    /// </remarks>
    internal CancellationToken InlineOperationCancellationToken => SchedulesShieldedChildren ? AttemptCancellationToken : CancellationToken;

    /// <summary>
    /// 当呼叫代码无法运行此执行的持久操作时丢弃 : <see cref="InvalidOperationException"/>
    /// 关闭执行的排程器或位于 <see cref="DurableTask.RunInline{TResult}(string, Func{TResult})"/>
    /// 和尝试取消时的 <see cref="DurableTaskAttemptCanceledException"/> 效果。
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
    /// 将 <see cref="InvalidOperationException"/> 投弃于这一处决的覆盖区域,其记录包括:
    /// 在那里运行的代码, 所以重播不会运行它。
    /// </summary>
    internal void ThrowIfInCoveredRegion()
    {
        if (IsInCoveredRegion)
        {
            throw new InvalidOperationException(OperationInCoveredRegionMessage);
        }
    }

    /// <summary>创建取消的尝试持久操作所丢出的一个例外 。</summary>
    /// <param name="innerException">取消操作时抛出的任何例外。</param>
    /// <exception cref="InvalidOperationException">尝试还没有取消</exception>
    protected internal DurableTaskAttemptCanceledException CreateAttemptCanceledException(Exception? innerException)
    {
        if (_attemptCancellationReason is not { } reason)
        {
            throw new InvalidOperationException("Only a canceled attempt throws the attempt's cancellation.");
        }

        return new(reason, AttemptCancellationToken, innerException);
    }

    /// <summary>
    /// 以 <see cref="DurableTaskAttemptCancellationReason.Diverged"/>取消此尝试, 因为它的重放会偏离
    /// 与先前的尝试记录中的处决结果 并返回了不同的操作所投出的例外
    /// </summary>
    /// <param name="cause">描述差异,例如代码不再预期其类型的结果记录。</param>
    /// <remarks>一旦部署的代码与记录再次匹配, 主机将重试任务。</remarks>
    internal DurableTaskAttemptCanceledException Diverge(Exception cause)
    {
        CancelAttempt(DurableTaskAttemptCancellationReason.Diverged);
        return CreateAttemptCanceledException(cause);
    }

    /// <summary>
    /// 返回 <paramref name="operation"/>,转换取消的取消,结束它,因为此尝试被取消
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
    /// 返回 <paramref name="operation"/>的回复,转换结束该回复或报告时的取消,因为
    /// 此尝试已取消至 <see cref="DurableTaskAttemptCanceledException"/>。
    /// </summary>
    internal ValueTask<DurableTaskResponse> ObserveAttempt(ValueTask<DurableTaskResponse> operation)
    {
        // ValueTask 能够一次性消耗,因此已完成的作业结果将一次性读取并返回到新的 ValueTask。
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
    /// 返回返回 <paramref name="response"/> 是否报告由于这一尝试而终止了操作的取消
    /// 取消, 操作随后将它丢弃为 <see cref="DurableTaskAttemptCanceledException"/>。
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
    /// 返回是否 <paramref name="exception"/> 关闭一个操作,因为调用者取消
    /// <paramref name="cancellationToken"/>,而不是因为主机取消了尝试。
    /// </summary>
    internal static bool IsCallerCancellation(OperationCanceledException exception, CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested && exception is not DurableTaskAttemptCanceledException;

    /// <summary>
    /// 要求持久取消这一背景下的任务,包括 <paramref name="reason"/>。
    /// </summary>
    /// <param name="reason">
    /// 为何没有报告 <see cref="TaskCancellationReason"/> 或 <see langword="null"/> 的理由。
    /// 确定原因, 所以重播放取消任务时重放的主机会传递它记录该任务的原因
    /// 取消。
    /// </param>
    /// <remarks>
    /// 主机对请求记录可持久保存,并记录执行处方首先观察的时间点。
    /// 当处决达到那个点时, 执行的调度器上写着: 在第一个运行期, 当请求到达的时候,
    /// 重播时,重放时会看到原运行时的取消。
    /// 请求设置 <see cref="IsTaskCancellationRequested"/> ,然后取消 <see cref="CancellationToken"/>,
    /// 登记同步运行,如同对任何 <see cref="CancellationTokenSource"/>一样。后来的请求更改
    /// 什么都没有。
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

    /// <summary>取消对 <paramref name="reason"/>的这一尝试。</summary>
    /// <param name="reason">为什么尝试被取消。</param>
    /// <remarks>
    /// 执行的调度器上调用主机呼叫此功能。 取消设置 <see cref="AttemptCancellationReason"/>, 然后
    /// 注销 <see cref="AttemptCancellationToken"/> ,然后 <see cref="CancellationToken"/>,登记运行
    /// 并肩同步 从那时起,尝试的持久操作
    /// <see cref="DurableTaskAttemptCanceledException"/> 之后的电话一无所有
    /// 东道国排干一切努力,放弃回应。
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

    /// <summary>断言主机在运行此执行的调度器上调用这个功能,而该设置是它自己的状态。</summary>
    private void AssertOnScheduler()
        => Debug.Assert(
            TaskScheduler.Current == _scheduler,
            "A durable execution's state is confined to the scheduler which was current when its host created its context.");

    /// <summary>
    /// 当任务代码从运行此执行的调度器中脱落时, 丢弃 <see cref="InvalidOperationException"/>
    /// 无法命令或记录其行动
    /// </summary>
    private void ThrowIfOffScheduler()
    {
        if (TaskScheduler.Current != _scheduler)
        {
            throw new InvalidOperationException(OffSchedulerMessage);
        }
    }

    /// <summary>
    /// 开始,或重新连接 <paramref name="taskDefinition"/> 下 <paramref name="taskId"/>,记录开始
    /// 执行中。
    /// </summary>
    /// <param name="taskId">
    /// 生儿 <see cref="Id"/> 所附任务或一
    /// <see cref="DurableTaskStartOptions.Detached"/> 任务。
    /// </param>
    /// <param name="taskDefinition">要开始的定义。</param>
    /// <param name="options">任务与执行过程以及开始完成时的关系如何。</param>
    /// <param name="cancellationToken">放弃开始的象征, 它不会取消任务 。</param>
    /// <returns>执行时观察任务的一个手柄。</returns>
    /// <remarks>
    /// <para>
    /// 主机记录执行树的起始点, 以及任务开始, 所以一个重放到
    /// 附加任务就是孩子
    /// <paramref name="taskId"/>,该执行人拥有:取消该处决后,儿童将获得其被撤销,除非开始
    /// 被遮蔽,正如 <see cref="SchedulesShieldedChildren"/> 报告的那样。一个独立的任务是一个独立的根根,没有
    /// 取消、中止或删除此执行的执行到。在独立任务中的执行名
    /// 以订阅者开始, 所以返回的控点会收到任务结果; 取消该控点, 或者此
    /// 执行中,仅结束订阅。
    /// </para>
    /// <para>
    /// 在一个标识符下开始,执行时记录在该标识符下,当定义发生时,该标识符与所记录的任务重新连接
    /// 同一请求, 并返回其处理。 在所记录的识别符号下的另一个请求是差异:
    /// 主机不启动便以 <see cref="DurableTaskAttemptCancellationReason.Diverged"/> 取消尝试 。
    /// </para>
    /// <para>
    /// 一旦启动时间持久,返回的操作即完成。
    /// <see cref="DurableTaskStartOptions.WaitForAcceptance"/>,一旦任务所在东道方也接受并完成
    /// 长期保存任务,或知道任务的结果,这样任务的身份识别特征可以交给其他当事人
    /// 来解决这个问题。
    /// </para>
    /// </remarks>
    protected internal abstract ValueTask<IScheduledTaskHandle> StartTaskAsync(
        TaskId taskId,
        DurableTask taskDefinition,
        DurableTaskStartOptions options,
        CancellationToken cancellationToken);

    /// <summary>
    /// 阶段 <paramref name="taskDefinition"/> 开始阶段,在 <paramref name="taskId"/> 之下,并记录在此
    /// 执行时同步。
    /// </summary>
    /// <param name="taskId">
    /// 生儿 <see cref="Id"/> 所附任务或一
    /// <see cref="DurableTaskStartOptions.Detached"/> 任务。
    /// </param>
    /// <param name="taskDefinition">要开始的定义。</param>
    /// <param name="options"><see cref="DurableTaskStartOptions.None"/> 或 <see cref="DurableTaskStartOptions.Detached"/>。</param>
    /// <returns>执行时观察任务的一个手柄。</returns>
    /// <remarks>
    /// 主办方开始阶段和在呼叫代码栏转时的记录, 并承诺执行下一个主办方的耐久性
    /// 与该转弯时的状态变化一起写入。进入记录中的重放返回相同的
    /// 将任务与此执行相联系
    /// <see cref="StartTaskAsync"/> 描述。无法启动启动的主机同步保存此默认值
    /// 执行中,投出 <see cref="NotSupportedException"/>。
    /// </remarks>
    protected internal virtual IScheduledTaskHandle PostTask(
        TaskId taskId,
        DurableTask taskDefinition,
        DurableTaskStartOptions options)
        => throw new NotSupportedException("The durable execution host does not support posting tasks.");

    /// <summary>附表12. 截止 <paramref name="dueTime"/>.</summary>
    protected internal abstract ValueTask<DurableTaskResponse> ScheduleDelayAsync(
        TaskId taskId,
        DateTimeOffset dueTime,
        CancellationToken cancellationToken);

    /// <summary>
    /// 与尸体的最终代码同步 收到行刑时的终端响应
    /// </summary>
    /// <param name="response">身体的终端响应。</param>
    /// <remarks>
    /// <para>
    /// 运行机构在连续运行任何物体之前称为此名称的定义: a <see langword="async"/>
    /// 长期任务方法称它从完成状态机器的步数起,代表定义则称它
    /// 或与其工作同步的延续。
    /// </para>
    /// <para>
    /// 负责国家变更的东道主将机构与机构结果一起进行修改,例如日记 grain
    /// 状态, 在此阶段结果。 工作在身体最终代码之后进行, 例如一个承诺
    /// 操作请求, 然后将结果与代码所做的修改一起实施。 主机仍然接收
    /// 答复: <see cref="DurableTask.RunAsync(DurableTaskContext)"/>执行返回
    /// 丢弃。 默认执行无任何作用 。
    /// </para>
    /// </remarks>
    protected internal virtual void OnBodyCompleted(DurableTaskResponse response)
    {
    }

    /// <summary>
    /// 等待执行中包含该执行的引用任务中名为 <paramref name="name"/> 的信号,
    /// 返回一个成功响应, 显示信号的记录值。
    /// </summary>
    /// <remarks>
    /// 主机记录了它为每个引用任务的名称而收到的第一个值, 以及该任务的状况,
    /// 并返回到该名字的每一个等待位置, 包括重放中的等待。 等待前发出的信号
    /// 立即完成。取消 <paramref name="cancellationToken"/> 后放弃等待。不执行的东道主
    /// 支持信号将保留默认执行, 这会投出 <see cref="NotSupportedException"/>。
    /// </remarks>
    protected internal virtual ValueTask<DurableTaskResponse> WaitForSignalAsync(
        string name,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("The durable execution host does not support signals.");

    /// <summary>
    /// 返回一名已完成的候选人,每次通话时返回同一候选人
    /// <paramref name="decisionId"/>,包括重播时的电话
    /// </summary>
    /// <remarks>
    /// <para>
    /// 候选人是先完成的:东道主根据 <paramref name="decisionId"/>继续挑选,
    /// 或从耐久的完工单中提取,例如每次在东道方
    /// 长期秩序不需要记录:完成的候选人在每一候选人中顺序最低的完整候选人相同。
    /// 重播,因为后来完成的候选人得到更高的顺序。
    /// </para>
    /// <para>
    /// <see cref="DurableTask.WhenAny(IReadOnlyList{DurableTask})"/> 以一个呼唤选择赢家,
    /// <see cref="ScheduledTask.WhenEach(IReadOnlyList{ScheduledTask})"/> 选择下一项任务,在
    /// 其余候选人,根据不同的决定识别符号。 <paramref name="candidates"/> 仅在
    /// 电话
    /// </para>
    /// <para>
    /// 主机返回一个选择,即使重播的候选人不再包含该选择,但当重放的候选人不再包含它时,主机仍会记录此选择,因为
    /// 代码已更改。调用操作后取消尝试
    /// <see cref="DurableTaskAttemptCancellationReason.Diverged"/>,因此任务将一直等待到符合代码
    /// 记录重播它。
    /// </para>
    /// </remarks>
    protected internal abstract ValueTask<TaskId> SelectCompletionAsync(
        TaskId decisionId,
        IReadOnlyList<TaskId> candidates,
        CancellationToken cancellationToken);

    /// <summary>
    /// 创建此执行下次操作的标识符, 使用提供时 <paramref name="name"/> 。
    /// </summary>
    /// <remarks>
    /// 重复一次的一个名字按顺序标明其后来发生的情况:第一个是 <paramref name="name"/>,
    /// 后附其节文,如 <c>reserve$2</c>,以便被环名的儿童保留其识别资料。
    /// 重放。 命名儿童, 命名组合器, 以及 <see cref="DurableTask.RunInline{TResult}(string, Func{TResult})"/>
    /// 效果是每个名字共一个数。名称不包含 <c>$</c>,而该名称生成了识别符的使用。
    /// 和未命名 <see cref="DurableTask.RunInline{TResult}(Func{TResult})"/> 效果共产生一个数
    /// 身份识别符号, <c>$child-1</c>、 <c>$child-2</c>等。
    /// 以相同的确定顺序创建操作。
    /// <see cref="DurableTask.WhenAll(IReadOnlyList{DurableTask})"/> 和
    /// <see cref="DurableTask.WhenAny(IReadOnlyList{DurableTask})"/>,其输入指数提供稳定的逻辑时段。
    /// 同时进行独立的时间安排工作,包括同时进行分组召集的祈祷、使用明确姓名或
    /// 以确定性顺序开始
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> 是空或包含 <c>$</c>.</exception>
    /// <exception cref="InvalidOperationException">呼叫代码在效果上运行,或从执行的排程器上运行。</exception>
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
    /// 获取执行此执行的东道主的身份,例如其 grain,它符合根标识符
    /// 执行的源代码是它开始的分离任务,或 <see langword="null"/> 毫无理由。
    /// </summary>
    /// <remarks>
    /// 任务识别符在宿主内部是独特的,所以对两个宿主的处决可以共同执行。
    /// 开始针对目标执行分离任务, 其他主机也开始提供身份的任务,
    /// 衍生根标识符不同。
    /// </remarks>
    protected internal virtual string? HostId => null;

    /// <summary>
    /// 创建独立任务根标识符,此执行作为运行开始
    /// <paramref name="operationId"/>.
    /// </summary>
    /// <remarks>
    /// 根标识符是一个单段, 当主机有 <see cref="HostId"/>的路径时, 它会保存此路径的剩余路径 。
    /// 之后的 <paramref name="operationId"/>: <c>{HostId}/{Id}/{segment}</c>,其中每一部分
    /// 逃离其 <c>/</c> 和 <c>\</c> 年为 <see cref="TaskId"/> 不同的东道主、处决和操作
    /// 产生不同的根, 达到同一操作的重放产生相同的根, 而其他方则产生同样的根
    /// 和操作的部件
    /// </remarks>
    internal TaskId CreateDetachedRootId(TaskId operationId)
    {
        Debug.Assert(operationId.IsChildOf(Id), "A detached task's operation is a child of the starting execution.");
        var path = operationId.ToString();
        return TaskId.CreateRoot(HostId is { } hostId ? $"{TaskId.CreateRoot(hostId)}/{path}" : path);
    }

    /// <summary>创建未命名的组合器或选择类型 <paramref name="kind"/>.</summary>
    /// <remarks>
    /// 每种种类都计算自己的业务,例如 <c>$when-any-1</c> 和 <c>$when-any-2</c>,因此数量变化
    /// 一种操作不会改变另一种类型的识别特征。
    /// </remarks>
    internal TaskId CreateOperationId(string kind) => Id.Child($"${kind}-{NextOccurrence(GeneratedSegmentPrefix + kind).ToString(CultureInfo.InvariantCulture)}");

    /// <summary>
    /// 创建类型操作的标识符 <paramref name="kind"/> 中,该观察 <paramref name="subject"/>,
    /// 诸如儿童等待的暂停期: <c>{segment}${kind}-{n}</c>,其中 <c>{segment}</c> 主题的区段
    /// 和 <c>{n}</c> 计算有关该主题的此类操作。
    /// </summary>
    /// <remarks>
    /// 识别特征识别器仅取决于主题和该主题的早期操作,因此其他
    /// 而不是被处决的孩子 需要下一个识别码
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

    /// <summary>计算在尝试中发生的 <paramref name="key"/> ,并从 1</summary>返回其正则。
    private int NextOccurrence(string key)
    {
        ThrowIfOffScheduler();
        ThrowIfInCoveredRegion();
        _nameOccurrences ??= new(StringComparer.Ordinal);
        _nameOccurrences.TryGetValue(key, out var occurrence);
        _nameOccurrences[key] = ++occurrence;
        return occurrence;
    }

    /// <summary>校验一个调用者提供的名称,该名称不能包含生成识别符号使用的 <c>$</c> 。</summary>
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
    /// 获取调用代码是否在本次执行的覆盖区域内运行:
    /// 执行封面,所以重放返回记录而不运行代码。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 被覆盖区域是保存代码的机制之一,记录包含死刑执行历史之外的代码:
    /// <see cref="DurableTask.RunInline{TResult}(string, Func{TResult})"/> 效果,同步运行, 和主机
    /// 覆盖代码, 例如在日记状态上操作及其运行的回回调, 或者
    /// 其结果一记录覆盖整个等待阶段的无与伦比的机构,例如交易。
    /// </para>
    /// <para>
    /// 在区域内,作为该区域的一部分,一个区域内的一个嵌套效应直接产生,没有识别特征或记录。
    /// 和所有其他的持久操作,例如启动、延迟、信号等待、组合器或版本检查等
    /// 在它取用标识符之前丢弃 <see cref="InvalidOperationException"/> 。主机不记录区域的
    /// 进入州作为处决的自己。
    /// </para>
    /// </remarks>
    internal bool IsInCoveredRegion => _coveredDepth > 0 || (_coveredFlows > 0 && ReferenceEquals(CoveredFlow.Value, this));

    /// <summary>
    /// 输入此执行的同步覆盖区域, 当返回值被在同一区域内处置时结束
    /// 执行计划仪表上的代码同步运行
    /// </summary>
    internal CoveredRegion EnterCoveredRegion()
    {
        AssertOnScheduler();
        _coveredDepth++;
        return new(this);
    }

    /// <summary>在同步覆盖区域运行效果代表。</summary>
    internal TResult RunAsEffect<TState, TResult>(TState state, Func<TState, TResult> body)
    {
        using var region = EnterCoveredRegion();
        return body(state);
    }

    /// <summary>
    /// 运行非同步 <paramref name="body"/> ,作为执行整个过程的覆盖区域,包括
    /// 其等待的继续,直到其完成任务。
    /// </summary>
    /// <remarks>
    /// 尸体从行刑的调度器开始, 当此方法在身体被处死后恢复时, 区域结束于此区域
    /// 完成任务。其他执行过程, 尸体没有开始, 也没有覆盖。
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

        // 标记流入身体的无同步流, 这个方法的流体一旦返回就会恢复。
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

    /// <summary>结束一个同步覆盖区域,该区域已输入 <see cref="EnterCoveredRegion"/> 。</summary>
    internal readonly struct CoveredRegion(DurableTaskContext context) : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose() => context._coveredDepth--;
    }

    /// <summary>
    /// 创建此执行执行的命名操作的标识符: <paramref name="name"/> 首次发生于
    /// 尝试的计算是 <c>{name}</c>,以后每次发生都附加其序号,例如 <c>reserve$2</c>。
    /// </summary>
    /// <param name="name">呼叫者提供的名称。</param>
    /// <remarks>
    /// 发生次数按试图制造它们的顺序编号,所以是产生同一名字的重放
    /// 在同一顺序下,同一顺序中的操作指定相同的识别特征,而添加后一事件时不会更早重命名
    /// 儿童、组合体和效果每个名字一个数,所以出现一个名字也是一样的
    /// 找到另一种操作的宿主,该主机在
    /// 用于报告重放的差异而不是运行操作的 。
    /// </remarks>
    internal TaskId CreateNamedChildId(string name)
    {
        var occurrence = NextOccurrence(name);
        return Id.Child(occurrence == 1
            ? name
            : $"{name}{GeneratedSegmentPrefix}{occurrence.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// 返回效果 <paramref name="effectId"/>记录的结果,或运行结果并记录其结果。
    /// </summary>
    /// <typeparam name="TState">国家类型 <paramref name="effect"/> 接收。</typeparam>
    /// <param name="effectId">效果的识别特征, 儿童这次处决。</param>
    /// <param name="state"> <paramref name="effect"/> 接收国。</param>
    /// <param name="effect">
    /// 运行效果并返回结果:结果,或它投出的例外。它不投出。
    /// </param>
    /// <returns>效果的结果, 东道方记录下来。</returns>
    /// <remarks>
    /// <para>
    /// <see cref="DurableTask.RunInline{TResult}(string, Func{TResult})"/> 与任务身体同步调用此功能。 东道主
    /// 记录在相同的同步工作中运行效果并分阶段进行记录,因此记录与下一个耐用
    /// 与效果所生成的状态变化一起写入。 身体的每个后期运行, 包括重放,
    /// 接收记录的结果时不运行效果。
    /// </para>
    /// <para>
    /// 无法存储结果的主机, 例如因为其结果不序列化, 记录和返回
    /// 失败, 所以每次运行都观察到同样的失败。 一个主机投出 <see cref="InvalidOperationException"/>
    /// 当它无法在记录中播放效果时, 运行效果而不运行效果。 例如当它不能在状态变化中进行该效果的状态变化时
    /// 调离主机的调度器。 当一个较早运行记录到另一种操作, 例如一步, 在
    /// 效果的标识符,重放与记录的执行相区别:主机取消了
    /// <see cref="DurableTaskAttemptCancellationReason.Diverged"/> ,但无效果,并保持任务待定。
    /// 不支持记录效果的主机保留此默认执行, 即丢弃
    /// <see cref="NotSupportedException"/>.
    /// </para>
    /// </remarks>
    protected internal virtual DurableTaskResponse RunEffect<TState>(
        TaskId effectId,
        TState state,
        Func<TState, DurableTaskResponse> effect)
        => throw new NotSupportedException("The durable task host does not support DurableTask.RunInline.");

    /// <summary>
    /// 返回此执行之后的修改 <paramref name="name"/> 版本,决定并记录
    /// 死刑执行处方的第一次呼叫 名字。
    /// </summary>
    /// <param name="name">更改的验证名称。</param>
    /// <param name="minSupported">呼叫代码支持的最老版本, 至少 <c>0</c>。</param>
    /// <param name="maxSupported">最新版本,至少 <paramref name="minSupported"/>。</param>
    /// <returns>执行版本,来自 <paramref name="minSupported"/> 至 <paramref name="maxSupported"/>.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="DurableTask.GetVersion(string, int, int)"/> 与任务身体同步调用此功能。 东道主
    /// 返回执行记录或执行记录所附执行记录;否则它决定
    /// <c>0</c> ,当先前的尝试记录了尚未达到的操作时,
    /// <paramref name="maxSupported"/> 当他们没有, 记录下决定与处决。
    /// </para>
    /// <para>
    /// 主机取消尝试 <see cref="DurableTaskAttemptCancellationReason.UnsupportedVersion"/> 时
    /// 执行的版本不在支持范围外, 丢弃
    /// <see cref="DurableTaskAttemptCanceledException"/> 其内心的例外,是
    /// <see cref="DurableTaskVersionException"/>;任务待定。不支持版本的东道主保留此任务
    /// 设置默认值,它会丢弃 <see cref="NotSupportedException"/>。
    /// </para>
    /// </remarks>
    protected internal virtual int GetVersion(string name, int minSupported, int maxSupported)
        => throw new NotSupportedException("The durable task host does not support DurableTask.GetVersion.");

    /// <summary>返回此运行尝试所遵循的修改版本 <paramref name="name"/> 。</summary>
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
