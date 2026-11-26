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
/// 订阅后,可以持续执行,等待任务的最终结果,但这项工作并非作为附件启动
/// 以 子: 一项 <c>GetDurableTask</c> 所处理的现有援引任务,或执行开始的单独任务。
/// 签名人 grain 被处决的儿童记录,其指纹为任务的名称 grain 和识别符号,以及
/// 在同一日记的订阅中,分期订入:带有儿童电线识别器的援引信息,因此
/// 既未要求任务,也未确认其打电话者的身份,而该人应履行一项单独任务的要求。
/// 儿童是取消的叶子:它自己取消,它的祖先取消,它的树被中止或删除
/// 其树树树在未完成任务的情况下将其终端化,并逐步取消订阅,即取消信件,命名
/// 儿童。
/// </para>
/// <para>
/// 任务 grain 的任务将儿童增列入任务的完成目的地,与任务请求者一起,因此任务
/// 其到达引言人手中时,即通过发箱,将任务保留到完成退休。
/// 订阅终端任务即时发送完成的信号, 并发送给 grain 无法保留的任务
/// 被引用的任务同时收到 <see cref="DurableTaskNotFoundException"/> 完成的完整。
/// 长期执行可维持的,是指其 grain 已经接受的任务:客户的时间表、
/// grain,或等待的持久处决,或处决本身开始的单独任务,其名称被援引
/// 直接在任务状态下订阅同一 grain 的任务,并在撰写时填写完成
/// 保存任务结果的 。
/// </para>
/// </remarks>
internal sealed partial class DurableTaskGrainRuntime
{
    // 执行树记录从“$”开始,所以订阅时绝不与请求指纹相撞。
    private const string SubscriptionFingerprintPrefix = "$subscribe:";

    /// <summary>
    /// 阶段 : 在 <paramref name="taskId"/>下, 或进入其记录,不以书面形式填写, 并返回
    /// 以及它是否进行了改变
    /// </summary>
    /// <remarks>
    /// 记录及其订阅承诺以一份日记写入, 因此重放时发现该记录时不订阅
    /// 还要等待早期尝试仍可等待的写作和记录的结果。
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

        // 取消的祖先的订阅不会创建, 除非父子关系启动保护 。
        if (IsStartRefusedByAncestorCancellation(taskId, state: null, shielded))
        {
            return new(new CompletedTaskHandle(taskId, DurableTaskResponse.Canceled), RequiresWrite: false);
        }

        // 留存在最后期限截止,所以订阅此 grain 的任务时不会发现保留结束的工作。
        DeleteExpiredTasks();
        DurableTaskResponse? response = null;
        if (subscription.Target == GrainId)
        {
            // 儿童在被编组之前读取订阅结果,因此不再解码的结果不能
            // 计划没有记录孩子
            response = SubscribeLocal(subscription.Id, taskId);
        }
        else
        {
            MessageTransport.SendSubscription(GrainId, subscription.Target, subscription.Id, GetWireId(taskId));
        }

        return new(StageSubscriptionRecord(taskId, DurableTaskRecordKind.Subscription, fingerprint, shielded, response), RequiresWrite: true);
    }

    /// <summary>
    /// 返回 <paramref name="taskId"/>或 <see langword="null"/>下记录的订阅子目录的处理
    /// 没有任何记录
    /// </summary>
    /// <remarks>
    /// 记录有 <paramref name="kind"/>、订阅 <paramref name="subscribedId"/>时,该记录即为同一操作
    /// 以 <paramref name="target"/>开始,对于独立启动,则开始 <paramref name="method"/>,不管其论点如何。
    /// </remarks>
    /// <exception cref="DurableTaskDivergenceException">记录有另一个操作。</exception>
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
    /// 将 grain的孩子 <paramref name="subscriberId"/> 列入 grain的任务 <paramref name="taskId"/>,以及
    /// 返回任务终端结果、未发现故障或任务尚未完成时的 <see langword="null"/> 。
    /// </summary>
    private DurableTaskResponse? SubscribeLocal(TaskId taskId, TaskId subscriberId) =>
        Subscribe(taskId, new(GrainId, subscriberId)) switch
        {
            SubscriptionOutcome.NotFound => CreateNotFound(taskId),
            SubscriptionOutcome.Terminal => _storage.TryGetTask(taskId, out var subscribed) ? subscribed.Result! : null,
            _ => null,
        };

    /// <summary>
    /// 阶段订阅儿童的记录,在订阅任务完成时以 <paramref name="response"/> 填写
    /// 是此 grain 并且将他的手柄还回去。
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

    /// <summary>添加信箱将发送到任务完成目的地的用户。</summary>
    /// <remarks>
    /// 订阅终端任务即时发送完成的信号, 订阅一个未引用的存储任务
    /// a 完成后 <see cref="DurableTaskNotFoundException"/>。重新交付的订阅不改变任何内容。
    /// </remarks>
    internal void SubscribeFromInbox(TaskId taskId, DurableTaskSubscriber subscriber)
    {
        ThrowIfStopping();
        if (subscriber.GrainId == GrainId)
        {
            throw new InvalidOperationException(
                $"Durable task '{subscriber.TaskId}' subscribes to a task of its own grain '{GrainId}' without durable messaging.");
        }

        // 保留在最后期限结束 所以订阅不会发现一个任务 保留结束了
        DeleteExpiredTasks();
        if (Subscribe(taskId, subscriber) == SubscriptionOutcome.NotFound)
        {
            MessageTransport.SendResponse(GrainId, subscriber, taskId, CreateNotFound(taskId));
        }
    }

    /// <summary>
    /// 删除任务完成前结束的订阅者。终端任务的完成已经未完成,
    /// 退休者按常例退休。
    /// </summary>
    /// <remarks>
    /// 发件箱独立提供订阅和结束。 到达的终点最先找不到用户, 所以没有用户
    /// 之后的订阅得到完成,而终止的儿童忽略了该完成。
    /// </remarks>
    internal void UnsubscribeFromInbox(TaskId taskId, DurableTaskSubscriber subscriber)
    {
        ThrowIfStopping();
        RemoveSubscriber(taskId, subscriber);
    }

    /// <summary>添加 <paramref name="subscriber"/> 至 <paramref name="taskId"/>完成目的地,没有书面文字。</summary>
    /// <returns>
    /// <see cref="SubscriptionOutcome.Pending"/> 当订阅者等待着任务结果的时候,
    /// <see cref="SubscriptionOutcome.Terminal"/> 当任务储存其结果时,或
    /// <see cref="SubscriptionOutcome.NotFound"/> 时, 如果此 grain 不保留该任务作为引用的任务。 订阅者
    /// 在一个 grain 终端任务通过发件箱接收完成的结束任务;此任务的订阅者 grain 读取
    /// 直接结果。
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

    /// <summary>删除一个任务订阅者, 即没有保存结果的任务, 不以书面形式保存 。</summary>
    /// <remarks>任务引用器不是一个用户, 而一个不订阅的用户会删除它。</remarks>
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
    /// 终止儿童订阅 <paramref name="subscriberId"/> 至 <paramref name="taskId"/> 联 年 月 日 月 日 月 月 日
    /// <paramref name="target"/>,因为孩子没有工作结果就结束了,例如当任务被取消时。
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
    /// 储存 <paramref name="response"/>,由于订阅 <paramref name="sourceTaskId"/>的结果
    /// 儿童 <paramref name="subscriberId"/>,在承诺后完成处理。
    /// </summary>
    /// <remarks>以树木删除或已经结束的儿童忽略了结果。</remarks>
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
    /// 返回任务是否是一个订阅子子进程,包括一个独立任务的记录及其所要执行的任务
    /// 订阅。
    /// </summary>
    private static bool TryGetSubscription(IDurableTaskState state, out GrainId target, out TaskId taskId) =>
        TryParseIdentity(SubscriptionFingerprintPrefix, state.RequestFingerprint, out target, out taskId)
        || TryGetDetachedTask(state, out target, out taskId);

    /// <summary>格式格式a grain 任务 <c>{prefix}{grain length}:{grain}{task}</c>毫不含糊地解释</summary>
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

    /// <summary>描述用户 <paramref name="taskId"/>:其 grain,然后是它的任务 当它不是 引用者的。</summary>
    private static string DescribeSubscriber(TaskId taskId, DurableTaskSubscriber subscriber) =>
        subscriber.TaskId == taskId ? subscriber.GrainId.ToString() : $"{subscriber.GrainId} {subscriber.TaskId}";
}
