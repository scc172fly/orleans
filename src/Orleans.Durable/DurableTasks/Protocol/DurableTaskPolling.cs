using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;

namespace Orleans.Durable.Tasks.Protocol;

/// <summary>
/// 将任务 grain从耐用执行之外的代码(如客户)中调出,并要求取消或中止任务。
/// 重新尝试使任务本身不受影响的种种失败。
/// </summary>
/// <remarks>
/// <para>
/// 民意测验是读取,取消和中止是一无所有,所以重复这些调查是安全的。未能达到 grain,
/// 例如,失去连接、停止或崩溃的 silo 、超时的呼叫或启动
/// 停止, 离开任务原样, 而下一次尝试达到其下次启动 grain。 这种失败是
/// 以指数反射重新审判,从 <see cref="InitialRetryDelay"/> 到 <see cref="MaxRetryDelay"/>,直到呼叫
/// 返回调用符号,或调用符号停止等待。任务结果,以及 <see cref="DurableTaskNotFoundException"/> a 的a
/// 任务,而 grain 没有保留,即时浮出水面。
/// </para>
/// <para>
/// 呼唤的召唤 grain 服务从它自己的运行时间 没有运输失败,也没有再尝试。
/// </para>
/// </remarks>
internal static class DurableTaskPolling
{
    internal static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(50);
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>投票 <paramref name="taskId"/> 日期 时 <paramref name="server"/>,重新尝试瞬时失败。</summary>
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
    /// 运行 <paramref name="request"/>,即向 <paramref name="server"/> 提出不称职的请求,例如取消或
    /// 中止, 重试瞬态失败 。
    /// </summary>
    /// <remarks>
    /// 每一通呼的速率 grain 不论 <paramref name="cancellationToken"/>,而它只结束
    /// 呼叫者等待,因此已经收到的 grain 请求生效。
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

    /// <summary>等待 <paramref name="delay"/> 并返回下一个,翻倍到 <see cref="MaxRetryDelay"/>。</summary>
    private static async ValueTask<TimeSpan> BackOffAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken);
        return TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
    }

    /// <summary>
    /// 返回 <paramref name="exception"/> 报告未达到任务 grain,而不是任务
    /// 或呼叫者等待的结束
    /// </summary>
    internal static bool IsTransient(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        // 包括 SiloUnavailableException,以及拒绝启动失败或移动的电话。
        OrleansMessageRejectionException => true,
        ConnectionFailedException or GatewayTooBusyException or TimeoutException => true,

        // grain运行时拒绝呼叫,在启动停止时无法等待。
        DurableTaskAttemptCanceledException { Reason: DurableTaskAttemptCancellationReason.ActivationStopping } => !cancellationToken.IsCancellationRequested,
        _ => false,
    };
}
