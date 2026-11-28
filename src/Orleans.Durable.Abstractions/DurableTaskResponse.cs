using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace Orleans.Durable.Tasks;

/// <summary>标识持久化任务的执行状态。</summary>
[Alias("Orleans.Durable.Tasks.DurableTaskStatus")]
public enum DurableTaskStatus
{
    /// <summary>没有可用的状态。</summary>
    None,
    /// <summary>任务尚未达到终态。</summary>
    Pending,
    /// <summary>任务已成功完成。</summary>
    CompletedSuccessfully,
    /// <summary>任务已被取消。</summary>
    Canceled,
    /// <summary>任务已失败。</summary>
    Failed,
}

/// <summary>标识持久化任务的观察结果类型。</summary>
[Alias("Orleans.Durable.Tasks.DurableTaskResponseKind")]
public enum DurableTaskResponseKind
{
    /// <summary>没有可用的观察结果。</summary>
    None,
    /// <summary>任务处于待处理状态。</summary>
    Pending,
    /// <summary>调用方已订阅未来的终态响应。</summary>
    Subscribed,
    /// <summary>任务已成功完成。</summary>
    CompletedSuccessfully,
    /// <summary>任务已被取消。</summary>
    Canceled,
    /// <summary>任务已失败。</summary>
    Failed,
}

/// <summary>表示持久化任务的观察结果。</summary>
[Alias("Orleans.Durable.Tasks.DurableTaskResponse")]
public abstract class DurableTaskResponse
{
    internal DurableTaskResponse() { }

    /// <summary>获取不带返回值且成功完成的任务的响应。</summary>
    public static DurableTaskResponse Completed => SuccessDurableTaskResponse.Instance;

    /// <summary>获取待处理响应。</summary>
    public static DurableTaskResponse Pending => PendingDurableTaskResponse.Instance;

    /// <summary>获取已订阅响应。</summary>
    public static DurableTaskResponse Subscribed => SubscribedDurableTaskResponse.Instance;

    /// <summary>创建包含新的 <see cref="OperationCanceledException"/> 的取消响应。</summary>
    public static DurableTaskResponse Canceled => new CanceledDurableTaskResponse(new OperationCanceledException());

    /// <summary>创建取消响应。</summary>
    public static DurableTaskResponse FromCanceled(OperationCanceledException exception) => new CanceledDurableTaskResponse(exception);

    /// <summary>根据异常创建失败或取消响应。</summary>
    public static DurableTaskResponse FromException(Exception exception)
        => exception is OperationCanceledException canceled ? FromCanceled(canceled) : new ExceptionDurableTaskResponse(exception);

    /// <summary>创建包含 <paramref name="value"/> 的成功响应。</summary>
    public static DurableTaskResponse<TResult> FromResult<TResult>(TResult value) => new(value);

    /// <summary>获取响应类型。</summary>
    public abstract DurableTaskResponseKind ResponseKind { get; }

    /// <summary>获取无类型的结果；如果任务未成功完成，则抛出异常。</summary>
    [SuppressMessage("Naming", "CA1721:Property names should not match get methods", Justification = "Result provides untyped inspection while GetResult<T> validates and returns a typed result.")]
    public abstract object? Result { get; }

    /// <summary>获取声明的结果类型（如果有）。</summary>
    public virtual Type? ResultType => null;

    /// <summary>获取终态异常（如果有）。</summary>
    public abstract Exception? Exception { get; }

    /// <summary>以 <typeparamref name="T"/> 类型返回结果；如果结果不可用或类型不兼容，则抛出异常。</summary>
    /// <remarks>
    /// 如果成功响应的结果不是 <typeparamref name="T"/> 类型，则抛出 <see cref="InvalidCastException"/>。
    /// 在持久化任务内，该结果是已记录的操作结果，而该操作的当前代码期望另一种类型，因此
    /// 重放偏离了已记录的执行过程：以以下原因取消执行尝试：
    /// <see cref="DurableTaskAttemptCancellationReason.Diverged"/>，任务保持待处理状态，直到部署后的代码
    /// 再次与记录匹配；并抛出 <see cref="DurableTaskAttemptCanceledException"/>，其内部异常是
    /// 上述 <see cref="InvalidCastException"/>。
    /// </remarks>
    public abstract T GetResult<T>();

    /// <summary>获取一个值，指示响应是否为终态响应。</summary>
    public bool IsCompleted => ResponseKind is DurableTaskResponseKind.CompletedSuccessfully
        or DurableTaskResponseKind.Canceled
        or DurableTaskResponseKind.Failed;

    /// <summary>获取此响应表示的执行状态。</summary>
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

    /// <summary>创建用于访问未完成响应时抛出的标准异常。</summary>
    protected static InvalidOperationException Incomplete()
        => new("The durable task has not completed.");

    /// <summary>
    /// 当以不符合实际类型的类型读取结果时创建相应异常：在持久化任务内，这表示重放与其记录发生分歧，
    /// 因而取消执行尝试；否则为 <see cref="InvalidCastException"/>。
    /// </summary>
    private protected static Exception ResultTypeMismatch(string message)
    {
        var mismatch = new InvalidCastException(message);
        return DurableTaskContext.Ambient is { } context ? context.Diverge(mismatch) : mismatch;
    }
}

/// <summary>表示没有返回值的成功持久化任务。</summary>
[Alias("Orleans.Durable.Tasks.SuccessDurableTaskResponse")]
public sealed class SuccessDurableTaskResponse : DurableTaskResponse
{
    /// <summary>获取共享的成功响应。</summary>
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

/// <summary>表示具有返回值的成功持久化任务。</summary>
[Alias("Orleans.Durable.Tasks.DurableTaskResponse`1")]
public sealed class DurableTaskResponse<TResult>(TResult result) : DurableTaskResponse
{
    /// <summary>获取具有类型的结果。</summary>
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

/// <summary>表示失败的持久化任务。</summary>
[Alias("Orleans.Durable.Tasks.ExceptionDurableTaskResponse")]
public sealed class ExceptionDurableTaskResponse : DurableTaskResponse
{
    /// <summary>为 <paramref name="exception"/> 初始化响应。</summary>
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

/// <summary>表示已取消的持久化任务。</summary>
[Alias("Orleans.Durable.Tasks.CanceledDurableTaskResponse")]
public sealed class CanceledDurableTaskResponse : DurableTaskResponse
{
    /// <summary>为 <paramref name="exception"/> 初始化响应。</summary>
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

/// <summary>表示待处理的持久化任务。</summary>
[Alias("Orleans.Durable.Tasks.PendingDurableTaskResponse")]
public sealed class PendingDurableTaskResponse : DurableTaskResponse
{
    /// <summary>获取共享的待处理响应。</summary>
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

/// <summary>表示对未来持久化任务响应的订阅。</summary>
[Alias("Orleans.Durable.Tasks.SubscribedDurableTaskResponse")]
public sealed class SubscribedDurableTaskResponse : DurableTaskResponse
{
    /// <summary>获取共享的已订阅响应。</summary>
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
