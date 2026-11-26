using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Messaging;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Runtime;

/// <remarks>
/// <para>
/// 持久执行从一个订阅儿童开始,通过记录其指纹名称为任务 grain的订阅儿童,开始一项独立的任务。
/// 和根标识符,以及谁的方法命名请求的方法, 并在同一日记中进行任务引用。
/// 儿童是订阅者,所以任务 grain 在存储该任务时将孩子添加到任务的完成目的地。
/// 儿童与其他订阅者一样执行该任务:其注销结束
/// 仅订阅。同一 grain 的单独任务直接在任务状态下进行。
/// </para>
/// <para>
/// 每一次在另一个 grain 上援引任务,都是一个参考发送,运行时间从它的日记表格中提供。
/// 表格与处决树分开,因此从开始到删除那些
/// 发件箱上报告 援引退休到运行时间, 从而从表格中删除
/// 将结果写入开始记录:呼叫另一台台 grain的记录,或
/// 发送信箱将任务完成后提供的传回 grain 他曾将这件事,加以长期的保存,
/// 目标是否接受。一个无法保存任务 grain ,因为其识别符号已经确认
/// 另一请求人或打电话者,将使用 <see cref="DurableTaskRejectionCodes.Conflict"/> 代码的编号写成死字,
/// 发箱将它退去,而它原是被拒绝的, grain原因,其中没有记录
/// <see cref="DurableTaskConflictException"/>. A grain 其进入窗口后收到传讯的,
/// <c>DeduplicationWindow</c> 开始阶段的援引 以死亡字母
/// <see cref="DurableTaskRejectionCodes.Expired"/> 代码。如果拒绝,有另一个代码或没有代码,记录失效
/// a <see cref="DurableTaskRejectedException"/>,报告代码。
/// 在邮件传送前确认 <c>OutboxRetryTimeout</c>,包括 grain 不接受,例如
/// 或哪些代码 grain 进入禁闭后,以死字字母退休,这不符合记录
/// 具有 a 的 <see cref="DurableTaskDeliveryException"/>:早先的尝试可能已经达到 grain所以结果就是
/// 未知。等待任务或等待接受的任务开始时,
/// 失败,在每次重播中。
/// </para>
/// </remarks>
internal sealed partial class DurableTaskGrainRuntime
{
    // 独立任务的记录是订阅儿童,其指纹为任务 grain 和根识别码,以及
    // 记录的方法识别请求,所以重放
    // 开始使用同一根标识符下的其他方法,但不同顺序的争论没有。
    private const string DetachedFingerprintPrefix = "$detach:";

    // 开始时间等待目标接受, 以记录识别码表示。
    // 记录接受或启动失败,或者删除记录。
    private readonly Dictionary<TaskId, TaskCompletionSource> _acceptanceWaiters = [];

    /// <summary>
    /// 启动 <paramref name="request"/> ,作为独立根任务 <paramref name="rootId"/>,记录并订阅于
    /// <paramref name="recordId"/>,等待开始的写作。
    /// </summary>
    /// <param name="waitForAcceptance">启动是否也等待任务 grain 接受任务。</param>
    public async ValueTask<IScheduledTaskHandle> StartDetachedAsync(
        TaskId recordId,
        TaskId rootId,
        IDurableTaskRequest request,
        bool shielded,
        bool waitForAcceptance,
        CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        cancellationToken.ThrowIfCancellationRequested();
        var handle = StageDetached(recordId, rootId, request, shielded);

        // 记录一开始就承诺,可能写在一份早期尝试仍在等待的书上。
        await WriteStateAsync(cancellationToken);
        if (waitForAcceptance)
        {
            await WaitForAcceptanceAsync(recordId, cancellationToken);
        }

        return handle;
    }

    /// <summary>
    /// 独立根任务 <paramref name="rootId"/>的启动阶段,在
    /// <paramref name="recordId"/>,不以书面形式。
    /// </summary>
    public IScheduledTaskHandle PostDetached(TaskId recordId, TaskId rootId, IDurableTaskRequest request, bool shielded)
    {
        ThrowIfStopping();
        return StageDetached(recordId, rootId, request, shielded);
    }

    /// <summary>
    /// 启动一个没有订阅者的分离根任务, 与在耐久执行之外运行的代码相连接。
    /// </summary>
    /// <remarks>
    /// 启动时要使用此 grain下个日记写入, 同时加上调用工作项的状态变化
    /// 任务的任务 grain 写完后开始。
    /// </remarks>
    public void PostRoot(TaskId rootId, IDurableTaskRequest request)
    {
        ThrowIfStopping();
        Debug.Assert(rootId.Parent().IsDefault, "A posted task is a root task.");
        CallerScope.ThrowIfReserved(rootId);
        var target = IDurableTaskRequest.GetContext(request).TargetId;
        if (target == GrainId)
        {
            StageScheduleWithCommitEffects(rootId, request, fromInbox: false);
            return;
        }

        PrepareDetachedInvocation(request);
        SendInvocation(MessageTransport.CreateDetachedInvocation(GrainId, target, rootId, request, subscriberId: null), request, recordId: null);
    }

    private IScheduledTaskHandle StageDetached(TaskId recordId, TaskId rootId, IDurableTaskRequest request, bool shielded)
    {
        Debug.Assert(rootId.Parent().IsDefault, "A detached task is a root task.");
        var target = IDurableTaskRequest.GetContext(request).TargetId;
        var method = IDurableTaskRequest.GetMethod(request);
        if (TryGetRecordedSubscription(recordId, DurableTaskRecordKind.DetachedStart, target, rootId, method, DescribeDetachedStart(target, rootId, method)) is { } recorded)
        {
            return recorded.Handle;
        }

        DurableTaskResponse? response = null;
        if (target == GrainId)
        {
            // 任务在记录前就已展开,因此与任务发生冲突的开始没有记录。
            StageScheduleWithCommitEffects(rootId, request, fromInbox: false);
            response = SubscribeLocal(rootId, recordId);
        }
        else
        {
            PrepareDetachedInvocation(request);
            var recordWireId = GetWireId(recordId);
            SendInvocation(
                MessageTransport.CreateDetachedInvocation(GrainId, target, rootId, request, recordWireId),
                request,
                recordWireId);
        }

        var fingerprint = FormatIdentity(
            $"{DetachedFingerprintPrefix}{IDurableTaskRequest.GetFingerprint(request, _shared.Serializer)}:",
            target,
            rootId);
        var handle = StageSubscriptionRecord(recordId, DurableTaskRecordKind.DetachedStart, fingerprint, shielded, response, method);

        // 取消的启动器仍然开始任务, 而取消的任务没有达到, 并且只结束订阅 。
        if (response is null && IsStartRefusedByAncestorCancellation(recordId, state: null, shielded))
        {
            _pendingHandleResponses[recordId] = StageResponse(recordId, DurableTaskResponse.Canceled);
        }

        return handle;
    }

    /// <summary>清除一个独立请求的来电人,但无人声称。</summary>
    private static void PrepareDetachedInvocation(IDurableTaskRequest request)
    {
        var context = IDurableTaskRequest.GetContext(request);
        context.CallerId = default;
        context.SupportsDurableCompletion = false;
    }

    /// <summary>返回任务是否是独立任务的记录及其开始的任务。</summary>
    private static bool TryGetDetachedTask(IDurableTaskState state, out GrainId target, out TaskId rootId)
    {
        target = default;
        rootId = default;
        if (state.RequestFingerprint is not { } fingerprint
            || !fingerprint.StartsWith(DetachedFingerprintPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        // 请求指纹在身份之前,没有“ : ” 。
        var separator = fingerprint.IndexOf(':', DetachedFingerprintPrefix.Length);
        return separator > 0 && TryParseIdentity(fingerprint[..(separator + 1)], fingerprint, out target, out rootId);
    }

    private static string DescribeDetachedStart(GrainId target, TaskId rootId, string? method) =>
        $"a start of detached task '{rootId}' on grain '{target}' for {DescribeMethod(method)}";

    /// <summary>
    /// 返回为什么 grain 拒绝援引 <paramref name="taskId"/> 其发件人首先在
    /// <paramref name="sentAt"/>或 <see langword="null"/> 接受时的 grain 。
    /// </summary>
    /// <remarks>
    /// grain 在 <see cref="DurableTaskGrainRuntimeShared.InvocationAdmissionWindow"/> 之前接受一次请求
    /// 发件人与 <paramref name="sentAt"/>之后或之后所装的注销或信号。
    /// 召回在 <see cref="DurableTaskGrainRuntimeShared.EarlyRecordRetention"/> 到达后登记,但
    /// 发送者时钟和 grain之间的折叠,所以被承认的祈祷找到它。就像通信一样
    /// <c>DeliverAt</c>,核对将发送器的时钟与 grain的时钟进行比较。
    /// 重新交付援引, grain 没有任何新阶段的存储,所以 grain 在任何时候都承认
    /// </remarks>
    internal string? GetInvocationAdmissionRejection(TaskId taskId, DateTimeOffset sentAt)
    {
        var deadline = AddSaturating(sentAt, _shared.InvocationAdmissionWindow);
        if (UtcNow < deadline || (_storage.TryGetTask(taskId, out var state) && IsInvoked(state)))
        {
            return null;
        }

        return $"The invocation of durable task '{taskId}' arrived at or after its admission deadline, {deadline:O}.";
    }

    /// <summary>阶段的援引, 信箱发送了该请求。</summary>
    /// <param name="taskId">所援引的任务:打电话者的子女,在打电话者的范围之内,或独立地执行根任务。</param>
    /// <param name="request">任务运行的请求。</param>
    /// <param name="sender">下 grain 他曾派遣祈祷者,</param>
    /// <param name="detached">是否援引开始一项独立的任务,而该任务没有呼叫者。</param>
    /// <param name="subscriberId">发送者任务中的线性识别符,如果有的话,该信号符可订阅一项独立的任务。</param>
    /// <remarks>
    /// 将独立任务用户添加到任务完成目的地,其内容与存储
    /// 任务。 与存储的任务发生冲突的援引,例如同一识别符号下的另一请求;
    /// 之前抛出任何东西, 所以水槽拒绝它 冲突的信息和代码, 信箱
    /// 记录已处理。
    /// </remarks>
    internal void InvokeFromInbox(TaskId taskId, IDurableTaskRequest request, GrainId sender, bool detached, TaskId? subscriberId)
    {
        ThrowIfStopping();
        var requestContext = request.Context
            ?? throw new InvalidOperationException("The durable task invocation request has no context.");
        if (detached)
        {
            // 分离任务是一个根任务, 而其他 grain的引用在保留范围根下没有产生。
            CallerScope.ThrowIfReserved(taskId);
            PrepareDetachedInvocation(request);
        }
        else if (subscriberId is not null)
        {
            throw new InvalidOperationException("An attached durable task invocation is addressed to its caller's task.");
        }
        else
        {
            requestContext.CallerId = sender;
            requestContext.SupportsDurableCompletion = true;
        }

        StageScheduleWithCommitEffects(taskId, request, fromInbox: true);
        if (subscriberId is { } subscriber)
        {
            var outcome = Subscribe(taskId, new(sender, subscriber));
            Debug.Assert(outcome != SubscriptionOutcome.NotFound, "A staged invocation is retained as an invoked task.");
        }
    }

    /// <summary>
    /// 发件箱中的阶段 <paramref name="invocation"/> ,参考文件编号,在待职职务表上予以记号
    /// 至退休时, <paramref name="recordId"/>,记录启动的任务的线条识别符号
    /// 如有结果的话。
    /// </summary>
    /// <remarks>
    /// 以相同身份连接的待查援引:重播一个恢复阶段没有恢复的开始。
    /// 记录所记录的起始点的身份覆盖了记录中的线标识符,因此每棵树的运行都发送到
    /// 尚未解决的援引保留了它所日记的请求,所以重述其论点的顺序不同
    /// 援引同一身份下另一种方法的待发援引,该同一身份先前曾被
    /// 的根任务,失败启动。
    /// </remarks>
    private void SendInvocation(OutboxMessage invocation, IDurableTaskRequest request, TaskId? recordId)
    {
        var messageId = invocation.Envelope.MessageId;
        if (_invocations.TryGetValue(messageId, out var pending))
        {
            if (!pending.Payload.AsSpan().SequenceEqual(invocation.Envelope.Payload)
                && !IsSameMethod(pending, request))
            {
                throw new InvalidOperationException(
                    $"An invocation of grain '{pending.Target}' with message ID '{messageId}' is still pending with a request for another method than {DescribeMethod(IDurableTaskRequest.GetMethod(request))}.");
            }

            return;
        }

        _invocations.Add(messageId, new(invocation.ReceiverId, invocation.Envelope.Payload, recordId));
        MessageTransport.Send(messageId);
    }

    /// <summary>返回待裁决的援引是否引用了 <paramref name="request"/> 援引的方法。</summary>
    /// <remarks>无法证明他的请求 使用同一方法</remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A request which doesn't decode isn't the same method.")]
    private bool IsSameMethod(PendingInvocation pending, IDurableTaskRequest request)
    {
        try
        {
            return _shared.Serializer.Deserialize<DurableTaskInvocationMessage>(pending.Payload)?.Request is { } pendingRequest
                && string.Equals(IDurableTaskRequest.GetMethod(pendingRequest), IDurableTaskRequest.GetMethod(request), StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 记录 <paramref name="invocation"/> 如何在开始的记录中留存:其目标是否接受,或
    /// 失败开始, 失败结束记录, 没有写入 。
    /// </summary>
    /// <remarks>
    /// 发件箱在同一个日记写时, 将取消援引的删除分期进行。 任务结果已经记录的
    /// 记录了当时的接受。记录中已经储存着它自己的结果,例如取消或取消。
    /// 中止, 保留它, 所以每个重播都观察它首先观察到的结果。 一个已张贴的根任务没有记录,
    /// 记录,用树的果木删除了它,而后出一棵树的记录,和那棵树的果实,也忽视了已老者已死的日子。
    /// 在同一识别特征下再生。
    /// </remarks>
    private void RetireInvocation(PendingInvocation invocation, OutboxMessageRetirement retirement)
    {
        if (invocation.RecordId is not { } recordWireId
            || !TryGetNode(recordWireId, out var recordId, out var state)
            || state.Accepted.HasValue)
        {
            return;
        }

        var delivered = retirement is OutboxMessageRetirement.Delivered;
        _storage.SetAccepted(recordId, state, delivered);
        if (!delivered && !HasStoredResult(state))
        {
            var invokedId = TryGetDetachedTask(state, out _, out var rootId) ? rootId : recordId;
            Exception failure = retirement switch
            {
                OutboxMessageRetirement.Rejected { Code: DurableTaskRejectionCodes.Conflict } rejected
                    => new DurableTaskConflictException(invokedId, rejected.Reason),
                OutboxMessageRetirement.Rejected rejected
                    => new DurableTaskRejectedException(invokedId, invocation.Target, rejected.Code, rejected.Reason),
                OutboxMessageRetirement.DeadLettered deadLettered
                    => new DurableTaskDeliveryException(invokedId, invocation.Target, deadLettered.Reason),
                OutboxMessageRetirement.Delivered => throw new UnreachableException("A delivered invocation has no failure."),
            };
            var response = DurableTaskResponse.FromException(failure);
            StoreResult(recordId, state, response, subscribed: true);
            _pendingHandleResponses[recordId] = response;
        }

        SignalAcceptance(recordId);
    }

    /// <summary>
    /// 除非 <paramref name="recordId"/>的铁丝标识符 <paramref name="wireId"/>启动投出
    /// <paramref name="sourceTaskId"/> 联 年 月 日 月 日 月 月 日 <paramref name="sender"/>:是边远的孩子 grain 库内
    /// 此 grain的调用范围, 或者它同意此项任务 。
    /// </summary>
    private void ThrowIfNotStartedBy(TaskId recordId, TaskId wireId, TaskId sourceTaskId, IDurableTaskState state, GrainId sender)
    {
        var matches = TryGetSubscription(state, out var target, out var subscribedTaskId)
            ? target == sender && subscribedTaskId == sourceTaskId
            : !state.RemoteTarget.IsDefault
                && state.RemoteRequestFingerprint is not null
                && state.RemoteTarget == sender
                && sourceTaskId == CallerScope.GetTaskId(GrainId, wireId);
        if (!matches)
        {
            throw new InvalidOperationException(
                $"Durable task '{recordId}' doesn't observe task '{sourceTaskId}' of grain '{sender}'.");
        }
    }

    /// <summary>
    /// 等到记录 <paramref name="recordId"/> 记录其启动目标是否接受
    /// 并抛出一个开始的结果 而它的目标不接受。
    /// </summary>
    /// <remarks>
    /// 此 grain 的开始一旦被写入承诺,便被接受。当另一个 grain 被引用时,它会被接受
    /// 完成时退休,或者当任务结果达到记录前先。失败的起始点以其
    /// 失败,或者记录本身的取消或中止, 它先储存。
    /// </remarks>
    private async ValueTask WaitForAcceptanceAsync(TaskId recordId, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (!_storage.TryGetTask(recordId, out var state))
            {
                throw new OperationCanceledException($"Durable task '{recordId}' was deleted with its execution tree.");
            }

            if (!AwaitsAcceptance(state))
            {
                return;
            }

            if (state.Accepted is { } accepted)
            {
                await WriteStateAsync(cancellationToken);
                if (!accepted)
                {
                    Debug.Assert(state.Result?.Exception is not null, "A failed start ends its record with a failure or a cancellation.");
                    ExceptionDispatchInfo.Throw(state.Result!.Exception!);
                }

                return;
            }

            if (!_acceptanceWaiters.TryGetValue(recordId, out var waiter))
            {
                _acceptanceWaiters[recordId] = waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            await waiter.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>返回记录是否开始另一个 grain的任务,等待接受。</summary>
    private bool AwaitsAcceptance(IDurableTaskState state) =>
        !state.RemoteTarget.IsDefault || (TryGetDetachedTask(state, out var target, out _) && target != GrainId);

    /// <summary>唤醒了等待答复的开始 <paramref name="recordId"/>,如果有的话。</summary>
    private void SignalAcceptance(TaskId recordId)
    {
        if (_acceptanceWaiters.Remove(recordId, out var waiter))
        {
            waiter.TrySetResult();
        }
    }
}
