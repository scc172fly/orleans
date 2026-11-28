using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Runtime;

/// <remarks>
/// <para>
/// 订阅使持久化执行能够等待并非由自身作为附接子任务启动的任务的终态
/// 结果：可以是通过 <c>GetDurableTask</c> 寻址的现有被调用任务，也可以是该执行启动的分离任务。
/// 订阅方 Grain 记录执行实例的一个子任务，其指纹标识任务所属 Grain 和标识符，并且
/// 在同一次日志写入中暂存订阅：调用消息携带子任务的传输标识符，因此它
/// 既不声明任务的调用方身份，也不验证调用方，并携带分离任务的请求。与延迟操作类似，
/// 子任务是取消传播的叶节点：其自身取消、祖先取消、任务树中止或删除
/// 其执行树都会使子任务进入终态而不影响被订阅任务，并暂存取消订阅操作，即指定
/// 该子任务的取消消息。
/// </para>
/// <para>
/// 任务所属 Grain 将子任务与任务调用方一起添加到完成通知的目标集合，使该任务的
/// 结果以与向调用方传递相同的方式到达订阅者：通过发件箱，并保留任务直到完成通知退役。
/// 订阅终态任务会立即发送其完成通知；订阅 Grain 未作为
/// 被调用任务保留的任务会立即收到携带 <see cref="DurableTaskNotFoundException"/> 的完成通知。持久化执行
/// 可以持有的任务标识符指向其 Grain 已接受的任务：客户端、
/// Grain 或持久化执行实例等待调度操作完成的任务，或者该执行实例自行启动的分离任务（其调用中指定了
/// 该子任务）。同一 Grain 上任务的订阅直接在任务状态中暂存，并在
/// 存储任务结果的同一次写入中完成。
/// </para>
/// </remarks>
internal sealed partial class DurableTaskGrainRuntime
{
    // 执行树记录以 '$' 开头，所以订阅不会与请求指纹发生冲突。
    private const string SubscriptionFingerprintPrefix = "$subscribe:";

    /// <summary>
    /// 在 <paramref name="taskId"/> 下暂存订阅子任务，或访问其记录，而不执行写入，并返回
    /// 它的句柄及是否暂存了变更。
    /// </summary>
    /// <remarks>
    /// 记录及其订阅在一次日志写入中提交，因此找到记录的重放不会再次订阅；
    /// 它等待该次写入（先前的执行尝试可能仍在等待），以及已记录的结果。
    /// </remarks>
    private StagedChild StageSubscription(TaskId taskId, IDurableTaskSubscription subscription, bool shielded)
    {
        var fingerprint = FormatIdentity(SubscriptionFingerprintPrefix, subscription.Target, subscription.Id);
        if (TryGetRecordedSubscription(
                taskId,
                DurableTaskRecordKind.Subscription,
                subscription.Target,
                subscription.Id,
                method: null,
                DescribeSubscription(subscription.Target, subscription.Id)) is { } recorded)
        {
            return recorded;
        }

        // 不会为已取消的祖先创建订阅，除非父任务以屏蔽保护方式启动它。
        if (IsStartRefusedByAncestorCancellation(taskId, state: null, shielded))
        {
            return new(new CompletedTaskHandle(taskId, DurableTaskResponse.Canceled), RequiresWrite: false);
        }

        // 保留期在截止时间结束，因此对本 Grain 上任务的订阅不会找到保留期已结束的任务。
        DeleteExpiredTasks();
        DurableTaskResponse? response = null;
        if (subscription.Target == GrainId)
        {
            // 暂存子任务之前读取被订阅任务的结果，因此无法再解码的结果会使
            // 调度失败，而不记录子任务。
            response = SubscribeLocal(subscription.Id, taskId);
        }
        else
        {
            MessageTransport.SendSubscription(GrainId, subscription.Target, subscription.Id, GetWireId(taskId));
        }

        return new(StageSubscriptionRecord(taskId, DurableTaskRecordKind.Subscription, fingerprint, shielded, response), RequiresWrite: true);
    }

    /// <summary>
    /// 返回在 <paramref name="taskId"/> 下记录的订阅子任务的句柄；若没有记录，则返回 <see langword="null"/>，
    /// 即空值。
    /// </summary>
    /// <remarks>
    /// 如果记录的类型为 <paramref name="kind"/>，订阅 <paramref name="subscribedId"/>
    /// 所标识、位于 <paramref name="target"/> 上的任务，并且对于分离启动，启动 <paramref name="method"/>，则为同一操作，与参数无关。
    /// </remarks>
    /// <exception cref="DurableTaskDivergenceException">记录保存的是另一项操作。</exception>
    private StagedChild? TryGetRecordedSubscription(
        TaskId taskId,
        DurableTaskRecordKind kind,
        GrainId target,
        TaskId subscribedId,
        string? method,
        string requested)
    {
        if (!_storage.TryGetTask(taskId, out var state))
        {
            return null;
        }

        if (state.Kind != kind
            || !TryGetSubscription(state, out var recordedTarget, out var recordedId)
            || recordedTarget != target
            || recordedId != subscribedId
            || !string.Equals(state.Method, method, StringComparison.Ordinal))
        {
            throw CreateDivergenceException(
                $"Durable child task '{taskId}' is already associated with a different operation.",
                state,
                requested);
        }

        if (ReadRecordedResult(taskId, state) is { IsCompleted: true } recorded)
        {
            return new(new CompletedTaskHandle(taskId, recorded), RequiresWrite: false);
        }

        if (state.CancellationRequestedAt.HasValue)
        {
            var canceled = StageResponse(taskId, DurableTaskResponse.Canceled);
            _pendingHandleResponses[taskId] = canceled;
            return new(new CompletedTaskHandle(taskId, canceled), RequiresWrite: true);
        }

        return new(GetScheduledTaskHandle(taskId), RequiresWrite: true);
    }

    /// <summary>
    /// 将本 Grain 的子任务 <paramref name="subscriberId"/> 订阅到本 Grain 的任务 <paramref name="taskId"/>，并
    /// 返回任务的终态结果、未找到任务的失败结果，或在任务待处理时返回 <see langword="null"/>。
    /// </summary>
    private DurableTaskResponse? SubscribeLocal(TaskId taskId, TaskId subscriberId) =>
        Subscribe(taskId, new(GrainId, subscriberId)) switch
        {
            SubscriptionOutcome.NotFound => CreateNotFound(taskId),
            SubscriptionOutcome.Terminal => _storage.TryGetTask(taskId, out var subscribed) ? subscribed.Result! : null,
            _ => null,
        };

    /// <summary>
    /// 暂存订阅子任务的记录；使用 <paramref name="response"/> 完成该子任务的条件是被订阅任务
    /// 位于本 Grain 且具有结果；随后返回子任务的句柄。
    /// </summary>
    private IScheduledTaskHandle StageSubscriptionRecord(
        TaskId taskId,
        DurableTaskRecordKind kind,
        string fingerprint,
        bool shielded,
        DurableTaskResponse? response,
        string? method = null)
    {
        var state = GetOrCreateTask(taskId, kind, request: null);
        _storage.SetRequestFingerprint(taskId, state, fingerprint, method);
        MarkShielded(taskId, state, shielded);
        var handle = GetOrCreateRunningTaskHandle(taskId);
        if (response is not null)
        {
            StoreResult(taskId, state, response, subscribed: true);
            _pendingHandleResponses[taskId] = response;
        }

        return handle;
    }

    /// <summary>将收件箱投递的订阅者添加到任务的完成通知目的地。</summary>
    /// <remarks>
    /// 订阅终态任务会立即发送其完成通知；订阅已存储但不是被调用任务的任务会发送
    /// 携带 <see cref="DurableTaskNotFoundException"/> 的完成通知。重新投递的订阅不会产生任何变更。
    /// </remarks>
    internal void SubscribeFromInbox(TaskId taskId, DurableTaskSubscriber subscriber)
    {
        ThrowIfStopping();
        if (subscriber.GrainId == GrainId)
        {
            throw new InvalidOperationException(
                $"Durable task '{subscriber.TaskId}' subscribes to a task of its own grain '{GrainId}' without durable messaging.");
        }

        // 保留期在截止时间结束，因此订阅不会找到保留期已结束的任务。
        DeleteExpiredTasks();
        if (Subscribe(taskId, subscriber) == SubscriptionOutcome.NotFound)
        {
            MessageTransport.SendResponse(GrainId, subscriber, taskId, CreateNotFound(taskId));
        }
    }

    /// <summary>
    /// 移除在任务完成前已结束订阅的订阅者。终态任务的完成通知已经待处理，
    /// 并照常退役。
    /// </summary>
    /// <remarks>
    /// 发件箱独立投递订阅及其结束消息。先到达的结束消息找不到订阅者，因此
    /// 紧随其后的订阅会收到完成通知，而已结束的子任务会忽略它。
    /// </remarks>
    internal void UnsubscribeFromInbox(TaskId taskId, DurableTaskSubscriber subscriber)
    {
        ThrowIfStopping();
        RemoveSubscriber(taskId, subscriber);
    }

    /// <summary>将 <paramref name="subscriber"/> 添加到 <paramref name="taskId"/> 的完成通知目的地，而不执行写入。</summary>
    /// <returns>
    /// <see cref="SubscriptionOutcome.Pending"/>：订阅者等待任务结果；
    /// <see cref="SubscriptionOutcome.Terminal"/>：任务已存储其结果；或
    /// <see cref="SubscriptionOutcome.NotFound"/>：本 Grain 未作为被调用任务保留该任务。对于终态任务，位于其他 Grain 上的
    /// 订阅者通过发件箱接收完成通知；本 Grain 上的订阅者直接读取
    /// 结果。
    /// </returns>
    private SubscriptionOutcome Subscribe(TaskId taskId, DurableTaskSubscriber subscriber)
    {
        if (!_storage.TryGetTask(taskId, out var state) || !IsInvoked(state))
        {
            return SubscriptionOutcome.NotFound;
        }

        if (HasStoredResult(state))
        {
            if (subscriber.GrainId != GrainId && TryRegisterCompletionDestination(taskId, state, subscriber))
            {
                SendCompletion(taskId, subscriber);
            }

            return SubscriptionOutcome.Terminal;
        }

        _storage.AddCompletionDestination(taskId, state, subscriber);
        return SubscriptionOutcome.Pending;
    }

    private enum SubscriptionOutcome
    {
        Pending,
        Terminal,
        NotFound,
    }

    /// <summary>移除尚未存储结果的任务的订阅者，而不执行写入。</summary>
    /// <remarks>任务的调用方不属于取消订阅操作所移除的订阅者。</remarks>
    private void RemoveSubscriber(TaskId taskId, DurableTaskSubscriber subscriber)
    {
        if (!_storage.TryGetTask(taskId, out var state)
            || HasStoredResult(state)
            || subscriber == GetInvoker(state.CallerId, taskId)
            || !state.CompletionDestinations.Contains(subscriber))
        {
            return;
        }

        _storage.RemoveCompletionDestination(taskId, state, subscriber);
    }

    /// <summary>
    /// 结束子任务 <paramref name="subscriberId"/> 对任务 <paramref name="taskId"/> 的订阅，该任务位于
    /// <paramref name="target"/>；原因是子任务在未获得被订阅任务结果的情况下结束，例如被取消。
    /// </summary>
    private void Unsubscribe(GrainId target, TaskId taskId, TaskId subscriberId)
    {
        if (target == GrainId)
        {
            RemoveSubscriber(taskId, new(GrainId, subscriberId));
        }
        else
        {
            MessageTransport.SendUnsubscription(GrainId, target, taskId, GetWireId(subscriberId));
        }
    }

    /// <summary>
    /// 将 <paramref name="response"/>（即 <paramref name="sourceTaskId"/> 的结果）存储为订阅
    /// 子任务 <paramref name="subscriberId"/> 的结果，在提交后完成其句柄。
    /// </summary>
    /// <remarks>已随任务树删除或已经结束的子任务会忽略该结果。</remarks>
    private void CompleteSubscription(TaskId subscriberId, TaskId sourceTaskId, DurableTaskResponse response)
    {
        if (!response.IsCompleted)
        {
            throw new InvalidOperationException("Only terminal durable task responses can complete a subscription.");
        }

        if (!_storage.TryGetTask(subscriberId, out var state) || HasStoredResult(state))
        {
            return;
        }

        Debug.Assert(
            TryGetSubscription(state, out _, out var subscribedTaskId) && subscribedTaskId == sourceTaskId,
            "A subscriber is a subscription child of the task.");
        StoreResult(subscriberId, state, response, subscribed: true);
        _pendingHandleResponses[subscriberId] = response;
    }

    private static DurableTaskResponse CreateNotFound(TaskId taskId) =>
        DurableTaskResponse.FromException(new DurableTaskNotFoundException(taskId));

    /// <summary>
    /// 返回任务是否是订阅子任务（包括分离任务的记录），以及它所
    /// 订阅的任务。
    /// </summary>
    private static bool TryGetSubscription(IDurableTaskState state, out GrainId target, out TaskId taskId) =>
        TryParseIdentity(SubscriptionFingerprintPrefix, state.RequestFingerprint, out target, out taskId)
        || TryGetDetachedTask(state, out target, out taskId);

    /// <summary>将 Grain 和任务格式化为 <c>{prefix}{grain length}:{grain}{task}</c>，可无歧义地解析。</summary>
    private static string FormatIdentity(string prefix, GrainId grain, TaskId taskId)
    {
        var grainId = grain.ToString();
        return $"{prefix}{grainId.Length.ToString(CultureInfo.InvariantCulture)}:{grainId}{taskId}";
    }

    private static bool TryParseIdentity(string prefix, string? value, out GrainId grain, out TaskId taskId)
    {
        grain = default;
        taskId = default;
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = value.AsSpan(prefix.Length);
        var separator = rest.IndexOf(':');
        if (separator <= 0
            || !int.TryParse(rest[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var length)
            || length > rest.Length - separator - 1)
        {
            return false;
        }

        rest = rest[(separator + 1)..];
        return GrainId.TryParse(rest[..length], provider: null, out grain)
            && TaskId.TryParse(rest[length..], out taskId);
    }

    private static string DescribeSubscription(GrainId target, TaskId taskId) =>
        $"a subscription to task '{taskId}' on grain '{target}'";

    /// <summary>描述 <paramref name="taskId"/> 的订阅者：先列出其 Grain；若其任务不同于调用方的任务，则随后列出其任务。</summary>
    private static string DescribeSubscriber(TaskId taskId, DurableTaskSubscriber subscriber) =>
        subscriber.TaskId == taskId ? subscriber.GrainId.ToString() : $"{subscriber.GrainId} {subscriber.TaskId}";
}
