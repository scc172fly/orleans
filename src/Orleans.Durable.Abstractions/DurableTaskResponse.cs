using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace Orleans.Durable.Tasks;

/// <summary>确定一项持久任务的执行状况。</summary>
[Alias("Orleans.Durable.Tasks.DurableTaskStatus")]
public enum DurableTaskStatus
{
    /// <summary>没有可用的状态 。</summary>
    None,
    /// <summary>任务不是终端。</summary>
    Pending,
    /// <summary>任务成功完成。</summary>
    CompletedSuccessfully,
    /// <summary>任务被取消。</summary>
    Canceled,
    /// <summary>任务失败 。</summary>
    Failed,
}

/// <summary>确定了持久任务观察的类型。</summary>
[Alias("Orleans.Durable.Tasks.DurableTaskResponseKind")]
public enum DurableTaskResponseKind
{
    /// <summary>没有观测结果。</summary>
    None,
    /// <summary>任务待定。</summary>
    Pending,
    /// <summary>呼叫者订阅未来终端响应。</summary>
    Subscribed,
    /// <summary>任务成功完成。</summary>
    CompletedSuccessfully,
    /// <summary>任务被取消。</summary>
    Canceled,
    /// <summary>任务失败 。</summary>
    Failed,
}

/// <summary>代表持久任务观察。</summary>
[Alias("Orleans.Durable.Tasks.DurableTaskResponse")]
public abstract class DurableTaskResponse
{
    internal DurableTaskResponse() { }

    /// <summary>获得对一项没有结果值的成功任务的反应。</summary>
    public static DurableTaskResponse Completed => SuccessDurableTaskResponse.Instance;

    /// <summary>得到等待的答复。</summary>
    public static DurableTaskResponse Pending => PendingDurableTaskResponse.Instance;

    /// <summary>得到订阅响应。</summary>
    public static DurableTaskResponse Subscribed => SubscribedDurableTaskResponse.Instance;

    /// <summary>创建一个拥有新 <see cref="OperationCanceledException"/>的取消响应。</summary>
    public static DurableTaskResponse Canceled => new CanceledDurableTaskResponse(new OperationCanceledException());

    /// <summary>创建取消响应的响应。</summary>
    public static DurableTaskResponse FromCanceled(OperationCanceledException exception) => new CanceledDurableTaskResponse(exception);

    /// <summary>创建例外响应失败或取消的响应。</summary>
    public static DurableTaskResponse FromException(Exception exception)
        => exception is OperationCanceledException canceled ? FromCanceled(canceled) : new ExceptionDurableTaskResponse(exception);

    /// <summary>创建包含 <paramref name="value"/>的中成功响应。</summary>
    public static DurableTaskResponse<TResult> FromResult<TResult>(TResult value) => new(value);

    /// <summary>得到的响应类。</summary>
    public abstract DurableTaskResponseKind ResponseKind { get; }

    /// <summary>获取未输入的结果,或者如果任务完成失败则丢弃。</summary>
    [SuppressMessage("Naming", "CA1721:Property names should not match get methods", Justification = "Result provides untyped inspection while GetResult<T> validates and returns a typed result.")]
    public abstract object? Result { get; }

    /// <summary>获得申报的结果类型,如果有的话。</summary>
    public virtual Type? ResultType => null;

    /// <summary>获得终端例外,如果有的话。</summary>
    public abstract Exception? Exception { get; }

    /// <summary>返回结果为 <typeparamref name="T"/>,如果没有或不兼容,则丢弃。</summary>
    /// <remarks>
    /// 成功的反应 其结果不是 <typeparamref name="T"/> 扔弃 <see cref="InvalidCastException"/>.
    /// 在一项持久任务中,结果就是记录到的作业结果,其代码现在预期会使用另一种类型,因此
    /// 重放与记录执行的相异:
    /// <see cref="DurableTaskAttemptCancellationReason.Diverged"/>,它保持任务直到部署代码
    /// 重新匹配记录, 而这个投 <see cref="DurableTaskAttemptCanceledException"/> 其内心的例外,是
    /// 排 <see cref="InvalidCastException"/>.
    /// </remarks>
    public abstract T GetResult<T>();

    /// <summary>获得一个数值,显示响应是否为终端。</summary>
    public bool IsCompleted => ResponseKind is DurableTaskResponseKind.CompletedSuccessfully
        or DurableTaskResponseKind.Canceled
        or DurableTaskResponseKind.Failed;

    /// <summary>获得该答复所代表的执行状态。</summary>
    public DurableTaskStatus Status => ResponseKind switch
    {
        DurableTaskResponseKind.None => DurableTaskStatus.None,
        DurableTaskResponseKind.Pending or DurableTaskResponseKind.Subscribed => DurableTaskStatus.Pending,
        DurableTaskResponseKind.CompletedSuccessfully => DurableTaskStatus.CompletedSuccessfully,
        DurableTaskResponseKind.Canceled => DurableTaskStatus.Canceled,
        DurableTaskResponseKind.Failed => DurableTaskStatus.Failed,
        _ => throw new InvalidOperationException($"Unknown response kind '{ResponseKind}'."),
    };

    internal void EnsureSuccessfulCompletion()
    {
        if (!IsCompleted)
        {
            throw new InvalidOperationException("The durable task has not completed.");
        }

        if (Exception is { } exception)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    /// <summary>创建获取不完整答复的标准例外。</summary>
    protected static InvalidOperationException Incomplete()
        => new("The durable task has not completed.");

    /// <summary>
    /// 创建结果的例外,结果被解读为它没有的类型:在耐久任务中,
    /// 其记录中可取消未遂,否则即为 <see cref="InvalidCastException"/>。
    /// </summary>
    private protected static Exception ResultTypeMismatch(string message)
    {
        var mismatch = new InvalidCastException(message);
        return DurableTaskContext.Ambient is { } context ? context.Diverge(mismatch) : mismatch;
    }
}

/// <summary>代表一项没有成果价值的、成功的持久任务。</summary>
[Alias("Orleans.Durable.Tasks.SuccessDurableTaskResponse")]
public sealed class SuccessDurableTaskResponse : DurableTaskResponse
{
    /// <summary>获得共同的成功回应。</summary>
    public static SuccessDurableTaskResponse Instance { get; } = new();

    private SuccessDurableTaskResponse() { }

    /// <inheritdoc />
    public override DurableTaskResponseKind ResponseKind => DurableTaskResponseKind.CompletedSuccessfully;

    /// <inheritdoc />
    public override object? Result => null;

    /// <inheritdoc />
    public override Exception? Exception => null;

    /// <inheritdoc />
    public override T GetResult<T>() => throw ResultTypeMismatch($"The completed task has no result value, not a '{typeof(T)}'.");
}

/// <summary>代表一个成功的耐久任务,且具有结果值。</summary>
[Alias("Orleans.Durable.Tasks.DurableTaskResponse`1")]
public sealed class DurableTaskResponse<TResult>(TResult result) : DurableTaskResponse
{
    /// <summary>获取输入结果。</summary>
    public TResult TypedResult => result;

    /// <inheritdoc />
    public override DurableTaskResponseKind ResponseKind => DurableTaskResponseKind.CompletedSuccessfully;

    /// <inheritdoc />
    public override object? Result => result;

    /// <inheritdoc />
    public override Type ResultType => typeof(TResult);

    /// <inheritdoc />
    public override Exception? Exception => null;

    /// <inheritdoc />
    public override T GetResult<T>()
    {
        if (result is T value)
        {
            return value;
        }

        if (result is null
            && default(T) is null
            && typeof(T).IsAssignableFrom(typeof(TResult)))
        {
            return default!;
        }

        throw ResultTypeMismatch($"The durable task result is '{typeof(TResult)}', not '{typeof(T)}'.");
    }
}

/// <summary>表示一个失败的耐久任务。</summary>
[Alias("Orleans.Durable.Tasks.ExceptionDurableTaskResponse")]
public sealed class ExceptionDurableTaskResponse : DurableTaskResponse
{
    /// <summary>初始化对 <paramref name="exception"/>的答复。</summary>
    public ExceptionDurableTaskResponse(Exception exception)
        => Exception = exception ?? throw new ArgumentNullException(nameof(exception));

    /// <inheritdoc />
    public override DurableTaskResponseKind ResponseKind => DurableTaskResponseKind.Failed;

    /// <inheritdoc />
    public override object? Result => GetResult<object?>();

    /// <inheritdoc />
    public override Exception Exception { get; }

    /// <inheritdoc />
    public override T GetResult<T>()
    {
        ExceptionDispatchInfo.Capture(Exception).Throw();
        return default!;
    }
}

/// <summary>表示一个取消的持久任务。</summary>
[Alias("Orleans.Durable.Tasks.CanceledDurableTaskResponse")]
public sealed class CanceledDurableTaskResponse : DurableTaskResponse
{
    /// <summary>初始化对 <paramref name="exception"/>的答复。</summary>
    public CanceledDurableTaskResponse(OperationCanceledException exception)
        => Exception = exception ?? throw new ArgumentNullException(nameof(exception));

    /// <inheritdoc />
    public override DurableTaskResponseKind ResponseKind => DurableTaskResponseKind.Canceled;

    /// <inheritdoc />
    public override object? Result => GetResult<object?>();

    /// <inheritdoc />
    public override OperationCanceledException Exception { get; }

    /// <inheritdoc />
    public override T GetResult<T>()
    {
        ExceptionDispatchInfo.Capture(Exception).Throw();
        return default!;
    }
}

/// <summary>代表一项待执行的持久任务。</summary>
[Alias("Orleans.Durable.Tasks.PendingDurableTaskResponse")]
public sealed class PendingDurableTaskResponse : DurableTaskResponse
{
    /// <summary>获得共享的待答回复。</summary>
    public static PendingDurableTaskResponse Instance { get; } = new();

    private PendingDurableTaskResponse() { }

    /// <inheritdoc />
    public override DurableTaskResponseKind ResponseKind => DurableTaskResponseKind.Pending;

    /// <inheritdoc />
    public override object? Result => throw Incomplete();

    /// <inheritdoc />
    public override Exception? Exception => null;

    /// <inheritdoc />
    public override T GetResult<T>() => throw Incomplete();
}

/// <summary>代表今后对持久任务回应的订阅。</summary>
[Alias("Orleans.Durable.Tasks.SubscribedDurableTaskResponse")]
public sealed class SubscribedDurableTaskResponse : DurableTaskResponse
{
    /// <summary>获得共享的订阅响应。</summary>
    public static SubscribedDurableTaskResponse Instance { get; } = new();

    private SubscribedDurableTaskResponse() { }

    /// <inheritdoc />
    public override DurableTaskResponseKind ResponseKind => DurableTaskResponseKind.Subscribed;

    /// <inheritdoc />
    public override object? Result => throw Incomplete();

    /// <inheritdoc />
    public override Exception? Exception => null;

    /// <inheritdoc />
    public override T GetResult<T>() => throw Incomplete();
}
