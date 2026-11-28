using Orleans.Durable.Tasks;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;

namespace Orleans.Durable.Http;

/// <summary>
/// 启动、观察、取消长时间运行的操作并向其发送信号，不依赖对外提供操作服务的具体协议。
/// </summary>
/// <remarks>
/// 操作是操作宿主 Grain 的根任务，以操作键为键，并订阅
/// 执行操作工作的任务：可以是持久化 Grain 调用，宿主在其 Grain 上将其作为根任务启动（以
/// 操作键命名），也可以是现有任务。各种协议（如本包自己的约定及 Nexus）从
/// 请求派生操作键，并将各自的请求和响应映射到这些操作。
/// </remarks>
internal sealed class DurableOperationService(IGrainFactory grainFactory, TimeProvider timeProvider)
{
    /// <summary>
    /// 单次轮询 Grain 的最长时长，也是等待操作宿主记录工作的最长时长。更长的等待采用
    /// 重复轮询，使每次轮询均在 Orleans 响应超时之前完成。
    /// </summary>
    internal static readonly TimeSpan MaxPollInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 启动 <paramref name="work"/>，将其作为操作 <paramref name="key"/>，或访问先前启动相同工作时创建的操作，
    /// 在操作进入终态或 <paramref name="wait"/> 到期时返回响应。
    /// </summary>
    /// <param name="key">操作的键。</param>
    /// <param name="work">
    /// 操作执行的持久化 Grain 调用，或对现有任务的引用（例如
    /// <see cref="DurableTaskReference{TResult}"/>），此时操作订阅该任务。
    /// </param>
    /// <param name="wait">等待操作完成的时长。</param>
    /// <param name="cancellationToken">用于放弃等待启动结果的取消令牌。</param>
    /// <remarks>
    /// 操作宿主保存操作并记录其工作后，启动请求即在 <see cref="MaxPollInterval"/> 内返回，
    /// 因此之后向操作发送信号或取消操作的请求能够到达其任务。如果启动请求执行相同工作，
    /// 则会访问现有操作：在同一 Grain 上调用同一方法（不考虑参数），或订阅
    /// 同一任务，符合持久化任务运行时标识操作的方式。
    /// </remarks>
    /// <exception cref="DurableTaskConflictException">操作 <paramref name="key"/> 执行的是其他工作。</exception>
    /// <exception cref="DurableOperationUnavailableException">操作宿主未在请求时限内响应。</exception>
    public async Task<DurableTaskResponse> StartAsync(string key, DurableTask work, TimeSpan wait, CancellationToken cancellationToken)
    {
        var host = GetHost(key);
        var requested = DurableOperationWork.Describe(work);
        var definition = requested.IsSubscription
            ? host.SubscribeAsync(requested.Address!.Value)
            : host.RunAsync(work);

        var started = timeProvider.GetTimestamp();
        ScheduledTask<DurableTaskResponse> scheduled;
        try
        {
            scheduled = await definition.WithId(DurableOperationGrain.RootId).ScheduleAsync(cancellationToken);
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            throw new DurableOperationUnavailableException(exception);
        }

        // 宿主日志记录操作执行的工作，用于标识该操作。如果宿主在
        // 记录工作之前结束（例如被中止），则报告宿主自身的失败。
        var recorded = await WaitForWorkAsync(host, MaxPollInterval);
        if (recorded is not null && !requested.IsSameWorkAs(recorded))
        {
            throw new DurableTaskConflictException(
                TaskId.CreateRoot(DurableOperationGrain.RootId),
                $"The operation '{key}' performs other work than the request: {recorded.Method} on {recorded.Target}.");
        }

        if (scheduled.IsCompleted)
        {
            return Unwrap(await scheduled.GetResponseAsync(cancellationToken));
        }

        if (recorded is null)
        {
            throw new DurableOperationUnavailableException();
        }

        var remaining = wait - timeProvider.GetElapsedTime(started);
        return await PollAsync(key, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, cancellationToken);
    }

    /// <summary>
    /// 在操作进入终态或 <paramref name="wait"/> 到期时返回响应；通过多次轮询其宿主，每次轮询时长
    /// 不超过 <see cref="MaxPollInterval"/>。
    /// </summary>
    /// <exception cref="DurableTaskNotFoundException">操作 <paramref name="key"/> 不存在或其保留期已结束。</exception>
    /// <exception cref="DurableOperationUnavailableException">操作宿主未在等待时限内响应。</exception>
    public async Task<DurableTaskResponse> PollAsync(string key, TimeSpan wait, CancellationToken cancellationToken)
    {
        // 对于不影响操作的故障（例如宿主正在停用），轮询会在截止时间前重试。
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(wait + MaxPollInterval);
        var operation = GetHost(key).GetDurableTask<DurableTaskResponse>(DurableOperationGrain.RootId);
        var started = timeProvider.GetTimestamp();
        try
        {
            while (true)
            {
                var remaining = wait - timeProvider.GetElapsedTime(started);
                var isLast = remaining <= MaxPollInterval;
                var interval = !isLast ? MaxPollInterval : remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
                var response = await operation.GetResponseAsync(new PollingOptions { PollTimeout = interval }, deadline.Token);
                if (response.IsCompleted || isLast)
                {
                    return Unwrap(response);
                }
            }
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new DurableOperationUnavailableException(exception);
        }
    }

    /// <summary>
    /// 持久化地请求取消正在运行的操作，携带 <paramref name="reason"/>，并返回
    /// 请求到达时操作的响应。
    /// </summary>
    /// <remarks>
    /// 取消采用协作式机制。执行工作的操作取消该工作的任务，由任务体决定
    /// 操作的结果。订阅现有任务的操作结束其订阅并报告自身已被
    /// 取消，而由其他方拥有的任务继续运行。终态操作保持不变，重复请求
    /// 保留首次取消原因。
    /// </remarks>
    /// <exception cref="DurableTaskNotFoundException">操作 <paramref name="key"/> 不存在或其保留期已结束。</exception>
    /// <exception cref="DurableOperationUnavailableException">操作宿主尚未记录其工作。</exception>
    public async Task<DurableTaskResponse> CancelAsync(string key, string? reason, CancellationToken cancellationToken)
    {
        var host = GetHost(key);
        var response = await PollAsync(key, TimeSpan.Zero, cancellationToken);
        if (response.IsCompleted)
        {
            return response;
        }

        var work = await WaitForWorkAsync(host, MaxPollInterval) ?? throw new DurableOperationUnavailableException();
        if (work.Address is not { } address)
        {
            // 操作在未运行工作的情况下失败，因此已进入终态。
            return await PollAsync(key, TimeSpan.Zero, cancellationToken);
        }

        try
        {
            if (work.IsSubscription)
            {
                await host.GetDurableTask<DurableTaskResponse>(DurableOperationGrain.RootId).CancelAsync(reason, cancellationToken);
            }
            else
            {
                await grainFactory.GetDurableTask<object?>(address).CancelAsync(reason, cancellationToken);
            }
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            throw new DurableOperationUnavailableException(exception);
        }

        return response;
    }

    /// <summary>向操作的工作传递 <paramref name="signal"/>，携带 <paramref name="value"/>，并返回其任务的处理结果。</summary>
    /// <exception cref="DurableTaskNotFoundException">操作 <paramref name="key"/> 不存在或其保留期已结束。</exception>
    /// <exception cref="DurableOperationUnavailableException">操作宿主尚未记录其工作。</exception>
    public async Task<DurableSignalResult> SignalAsync<T>(string key, DurableSignal<T> signal, T value, CancellationToken cancellationToken)
    {
        // 轮询确认操作存在，因此其宿主会记录该工作。已完成操作的工作任务
        // 保留收到的信号，因此重试信号可以找到其值。
        _ = await PollAsync(key, TimeSpan.Zero, cancellationToken);
        var work = await WaitForWorkAsync(GetHost(key), MaxPollInterval) ?? throw new DurableOperationUnavailableException();
        if (work.Address is not { } address)
        {
            return DurableSignalResult.TaskCompleted;
        }

        try
        {
            return await grainFactory.GetGrain(address.Target).TrySignalDurableTaskAsync(address.Id, signal, value, cancellationToken);
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            throw new DurableOperationUnavailableException(exception);
        }
    }

    /// <summary>返回承载操作 <paramref name="key"/> 的 Grain。</summary>
    public GrainId GetHostId(string key) => GetHost(key).GetGrainId();

    /// <summary>
    /// 返回操作工作的响应：即宿主根任务返回的响应；如果宿主失败（例如执行工作的 Grain 拒绝工作，
    /// 或宿主被中止），则返回宿主自身的响应。
    /// </summary>
    internal static DurableTaskResponse Unwrap(DurableTaskResponse response) =>
        response is { Status: DurableTaskStatus.CompletedSuccessfully, Result: DurableTaskResponse work } ? work : response;

    /// <summary>
    /// 返回 <paramref name="exception"/> 是否报告未能到达某个 Grain 的故障；此类故障不会改变
    /// 操作，因此客户端重试请求。
    /// </summary>
    internal static bool IsUnavailable(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        DurableOperationUnavailableException => true,

        // 包括 SiloUnavailableException，以及发往发生故障或迁移的激活实例的调用被拒绝的情况。
        OrleansMessageRejectionException or ConnectionFailedException or GatewayTooBusyException or TimeoutException => true,

        // Grain 的持久化任务运行时在激活实例停止期间拒绝调用。
        DurableTaskAttemptCanceledException { Reason: DurableTaskAttemptCancellationReason.ActivationStopping } => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };

    private static async Task<DurableOperationWork?> WaitForWorkAsync(IDurableOperationGrain host, TimeSpan timeout)
    {
        try
        {
            return await host.WaitForWorkAsync(timeout);
        }
        catch (Exception exception) when (IsUnavailable(exception, CancellationToken.None))
        {
            return null;
        }
    }

    private IDurableOperationGrain GetHost(string key) => grainFactory.GetGrain<IDurableOperationGrain>(key);
}

/// <summary>
/// 表示请求未到达操作，或者在宿主记录工作之前到达操作，因此
/// 未改变操作。客户端应重新发送请求。
/// </summary>
internal sealed class DurableOperationUnavailableException(Exception? innerException = null)
    : Exception("The operation is unavailable. Send the request again.", innerException);
