using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;

namespace Orleans.Durable.Tasks.Protocol;

/// <summary>
/// 从持久化执行实例之外的代码（例如客户端）轮询任务所属 Grain，并请求取消或中止任务，
/// 对不影响任务本身的失败进行重试。
/// </summary>
/// <remarks>
/// <para>
/// 轮询是读取操作，取消和中止是幂等操作，因此可以安全地重复执行。未能访问 Grain（例如
/// 连接丢失、Silo 停止或崩溃、调用超时，或激活实例正在
/// 停止）不会改变任务，下一次尝试将访问其 Grain 的下一次激活实例。此类失败
/// 采用指数退避重试，从 <see cref="InitialRetryDelay"/> 到 <see cref="MaxRetryDelay"/>，直到调用
/// 返回或调用方的令牌结束等待。任务的结果，以及 <see cref="DurableTaskNotFoundException"/>（对于
/// Grain 未保留的任务），都会立即呈现。
/// </para>
/// <para>
/// 调用方 Grain 通过自身运行时处理的调用没有可能发生故障的传输过程，因此不重试。
/// </para>
/// </remarks>
internal static class DurableTaskPolling
{
    internal static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(50);
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>轮询任务 <paramref name="taskId"/>（位于 <paramref name="server"/> 上），并重试瞬态失败。</summary>
    public static async ValueTask<DurableTaskResponse> PollAsync(
        IDurableTaskServer server,
        TaskId taskId,
        SubscribeOrPollOptions options,
        CancellationToken cancellationToken)
    {
        if (server is DurableTaskGrainRuntime)
        {
            return await server.SubscribeOrPollAsync(taskId, options, cancellationToken);
        }

        var delay = InitialRetryDelay;
        while (true)
        {
            try
            {
                return await server.SubscribeOrPollAsync(taskId, options, cancellationToken);
            }
            catch (Exception exception) when (IsTransient(exception, cancellationToken))
            {
                delay = await BackOffAsync(delay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 执行 <paramref name="request"/>，即发往 <paramref name="server"/> 的幂等请求（例如取消或
    /// 中止），并重试瞬态失败。
    /// </summary>
    /// <remarks>
    /// 无论 <paramref name="cancellationToken"/> 如何，每次调用都在 Grain 上运行至完成；该令牌仅结束
    /// 调用方的等待，所以 Grain 已收到的请求会生效。
    /// </remarks>
    public static async ValueTask SendAsync<TState>(
        IDurableTaskServer server,
        TState state,
        Func<IDurableTaskServer, TState, ValueTask> request,
        CancellationToken cancellationToken)
    {
        if (server is DurableTaskGrainRuntime)
        {
            await request(server, state).AsTask().WaitAsync(cancellationToken);
            return;
        }

        var delay = InitialRetryDelay;
        while (true)
        {
            try
            {
                await request(server, state).AsTask().WaitAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (IsTransient(exception, cancellationToken))
            {
                delay = await BackOffAsync(delay, cancellationToken);
            }
        }
    }

    /// <summary>等待 <paramref name="delay"/> 并返回下一次等待时长，即当前时长的两倍，但不超过 <see cref="MaxRetryDelay"/>。</summary>
    private static async ValueTask<TimeSpan> BackOffAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken);
        return TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
    }

    /// <summary>
    /// 返回 <paramref name="exception"/> 是否报告未能访问任务所属 Grain，而不是报告任务的
    /// 结果或调用方等待的结束。
    /// </summary>
    internal static bool IsTransient(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        // 包括 SiloUnavailableException，以及发往失败或迁移中的激活实例的调用遭拒的情形。
        OrleansMessageRejectionException => true,
        ConnectionFailedException or GatewayTooBusyException or TimeoutException => true,

        // Grain 的激活实例停止期间，其运行时拒绝调用，并使挂起的等待失败。
        DurableTaskAttemptCanceledException { Reason: DurableTaskAttemptCancellationReason.ActivationStopping } => !cancellationToken.IsCancellationRequested,
        _ => false,
    };
}
