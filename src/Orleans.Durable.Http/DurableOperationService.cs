using Orleans.Durable.Tasks;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;

namespace Orleans.Durable.Http;

/// <summary>
/// 启动、观察、取消和信号长期运行的行动,独立于暴露于它们的协议。
/// </summary>
/// <remarks>
/// 操作是运行主机 grain的根任务,由该操作的密钥键键键键键键键键,该密钥订阅此任务
/// 执行操作工作:耐久性 grain , 即主机以根开始于 grain 被命名为
/// 键或现有任务。
/// 请求书,并标明他们的请求和对这些行动的答复。
/// </remarks>
internal sealed class DurableOperationService(IGrainFactory grainFactory, TimeProvider timeProvider)
{
    /// <summary>
    /// 最长的 grain一次民意测验,最长的等待操作主机记录工作。
    /// 多次调查,因此每次调查都能够在 Orleans 反应超时内完成。
    /// </summary>
    internal static readonly TimeSpan MaxPollInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 开始 <paramref name="work"/> 操作 <paramref name="key"/>,或达到更早的操作
    /// 同一工作开始后, 当它为终端或 <paramref name="wait"/> 折叠时返回其响应 。
    /// </summary>
    /// <param name="key">操作的密钥。</param>
    /// <param name="work">
    /// 耐性 grain 运行的调用,或现有任务的引用,例如
    /// 业务处所认可的<see cref="DurableTaskReference{TResult}"/>。
    /// </param>
    /// <param name="wait">完成操作要等多久。</param>
    /// <param name="cancellationToken">放弃启动等待的标记。</param>
    /// <remarks>
    /// 一旦操作主机持有并记录了工作, 启动返回就会在 <see cref="MaxPollInterval"/>内,
    /// 启动当它到达一个现有操作时,它就会到达
    /// 同一工作:在同一 grain上用同样方法调用同一工作,不论其论点如何,或订阅
    /// 任务与持久任务运行时间确定的业务相同。
    /// </remarks>
    /// <exception cref="DurableTaskConflictException">操作操作 <paramref name="key"/> 从事其他工作。</exception>
    /// <exception cref="DurableOperationUnavailableException">操作的主机没有在请求的时间里回复</exception>
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

        // 主机主机日记记录操作所执行的工作, 以此识别操作。
        // 在它记录其工作之前,例如被中止的工作,报告自己的失败。
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
    /// 一旦业务在终点或 <paramref name="wait"/> 过错时返回其响应,然后通过投票将主机点点票
    /// 不超过 <see cref="MaxPollInterval"/>。
    /// </summary>
    /// <exception cref="DurableTaskNotFoundException">无操作 <paramref name="key"/> 或已经终止。</exception>
    /// <exception cref="DurableOperationUnavailableException">操作的主机在等待中没有回应</exception>
    public async Task<DurableTaskResponse> PollAsync(string key, TimeSpan wait, CancellationToken cancellationToken)
    {
        // 民意调查失败,使行动不受影响,如一个停机的东道方,直到最后期限。
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
    /// 可持久地请求取消运行中的操作, 包含 <paramref name="reason"/>并返回回复
    /// 请求到达时,该手术拥有的。
    /// </summary>
    /// <remarks>
    /// 取消是合作性的。运行一项操作,即工作运作,即取消该工程的任务,由该机构决定
    /// 订阅现有任务的行动结束其订阅并报告它曾经参加过
    /// 终止操作没有改变, 重复请求
    /// 保留第一个理由。
    /// </remarks>
    /// <exception cref="DurableTaskNotFoundException">无操作 <paramref name="key"/> 或已经终止。</exception>
    /// <exception cref="DurableOperationUnavailableException">行动的主机还没录下来</exception>
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
            // 操作未运行工作而失败, 所以它是终端 。
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

    /// <summary>交付交付 <paramref name="signal"/> 与 <paramref name="value"/> 返回到操作的工作,并返回它的任务是如何处理它的。</summary>
    /// <exception cref="DurableTaskNotFoundException">无操作 <paramref name="key"/> 或已经终止。</exception>
    /// <exception cref="DurableOperationUnavailableException">行动的主机还没录下来</exception>
    public async Task<DurableSignalResult> SignalAsync<T>(string key, DurableSignal<T> signal, T value, CancellationToken cancellationToken)
    {
        // 民意测验确定,该业务存在,因此其主机记录其工作。
        // 保留收到的信号 所以一个重新研究的信号 找到了它的价值
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

    /// <summary>返回 grain 操作所在的运行主机 <paramref name="key"/>.</summary>
    public GrainId GetHostId(string key) => GetHost(key).GetGrainId();

    /// <summary>
    /// 返回操作工作的反应: 其主机根任务返回的响应, 或者主机自己返回的响应
    /// 当主机失败时,例如当工程 grain 拒绝工作或主机中止时,主机失败了响应。
    /// </summary>
    internal static DurableTaskResponse Unwrap(DurableTaskResponse response) =>
        response is { Status: DurableTaskStatus.CompletedSuccessfully, Result: DurableTaskResponse work } ? work : response;

    /// <summary>
    /// 返回是否 <paramref name="exception"/> 未能达到 grain,它离开操作
    /// 所以客户重试了请求
    /// </summary>
    internal static bool IsUnavailable(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        DurableOperationUnavailableException => true,

        // 包括 SiloUnavailableException,以及拒绝启动失败或移动的电话。
        OrleansMessageRejectionException or ConnectionFailedException or GatewayTooBusyException or TimeoutException => true,

        // A grain的持久任务运行时间在启动停止时拒绝呼叫 。
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
/// 代表一个没有行动的请求,或在其东道方记录工作之前到达请求,因此离开
/// 客户再次发送请求。
/// </summary>
internal sealed class DurableOperationUnavailableException(Exception? innerException = null)
    : Exception("The operation is unavailable. Send the request again.", innerException);
