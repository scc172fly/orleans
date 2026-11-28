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
/// 持久化执行实例通过记录订阅子任务来启动分离任务，其指纹标识任务所属 Grain
/// 和根标识符，其方法标识请求的方法，并在同一次日志写入中暂存任务调用。调用会指定
/// 子任务作为订阅者，因此任务所属 Grain 在存储任务时将子任务添加到任务完成通知的目的地，
/// 无需单独订阅。该子任务像其他订阅一样观察任务：取消它会结束
/// 其订阅，而不取消被订阅任务。位于同一 Grain 的分离任务直接在任务状态中暂存。
/// </para>
/// <para>
/// 每次对其他 Grain 上任务的调用都属于引用发送，由运行时根据其日志化的
/// 待处理调用表提供调用。该表独立于执行树，因此即使删除了
/// 发起该调用的执行树，启动操作也仍然保留。发件箱向运行时报告调用的退役；运行时在
/// 退役的日志写入中将其从表中移除，并在启动记录上记录结果，即对另一 Grain 的调用记录，或
/// 分离任务的记录。任务所属 Grain 持久化存储任务后，发件箱将调用作为已投递退役，
/// 这表示目标接受了任务。若 Grain 无法存储任务（因为其标识符已经标识
/// 其他请求或调用方），则以 <see cref="DurableTaskRejectionCodes.Conflict"/> 代码将调用转为死信，
/// 发件箱将其作为拒绝结果退役，并携带 Grain 给出的原因，使记录以
/// <see cref="DurableTaskConflictException"/> 失败。Grain 收到超出其准入窗口的调用时（该窗口为自首次暂存调用起经过 Messaging 的
/// <c>DeduplicationWindow</c> 指定的时长），以
/// <see cref="DurableTaskRejectionCodes.Expired"/> 代码将调用转为死信。使用其他代码或无代码的其他拒绝使记录以
/// <see cref="DurableTaskRejectedException"/> 失败，该异常报告拒绝代码。若发件箱无法在 Messaging 的
/// <c>OutboxRetryTimeout</c> 期限内确认调用已投递，包括 Grain 未能接受调用（例如调用
/// 无法解码），或 Grain 在准入截止时间之后拒绝调用的情形，则调用作为死信退役，使记录
/// 以 <see cref="DurableTaskDeliveryException"/> 失败：由于之前的尝试可能已到达 Grain，所以结果
/// 未知。在每次重放中，等待任务或等待启动操作被接受都会抛出
/// 此失败。
/// </para>
/// </remarks>
internal sealed partial class DurableTaskGrainRuntime
{
    // 分离任务的记录是一个订阅子任务，其指纹标识任务所属 Grain 和根标识符，并且
    // 包含分离请求的诊断指纹。记录的方法标识请求，因此重放时如果
    // 在相同根标识符下启动另一个方法，会发生重放分歧；而参数序列化形式不同则不会导致分歧。
    private const string DetachedFingerprintPrefix = "$detach:";

    // 等待目标接受的启动操作，按其记录的标识符索引。每个启动操作在其记录
    // 记录接受状态、启动失败或记录被删除时收到通知。
    private readonly Dictionary<TaskId, TaskCompletionSource> _acceptanceWaiters = [];

    /// <summary>
    /// 将 <paramref name="request"/> 作为分离根任务 <paramref name="rootId"/> 启动，在
    /// <paramref name="recordId"/> 下记录并订阅，并等待提交启动操作的写入。
    /// </summary>
    /// <param name="waitForAcceptance">启动操作是否也等待任务所属 Grain 接受任务。</param>
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

        // 记录随启动操作一起提交，可能发生在先前执行尝试仍在等待的写入中。
        await WriteStateAsync(cancellationToken);
        if (waitForAcceptance)
        {
            await WaitForAcceptanceAsync(recordId, cancellationToken);
        }

        return handle;
    }

    /// <summary>
    /// 暂存分离根任务 <paramref name="rootId"/> 的启动操作，并在
    /// <paramref name="recordId"/> 下记录并订阅，但不执行写入。
    /// </summary>
    public IScheduledTaskHandle PostDetached(TaskId recordId, TaskId rootId, IDurableTaskRequest request, bool shielded)
    {
        ThrowIfStopping();
        return StageDetached(recordId, rootId, request, shielded);
    }

    /// <summary>
    /// 从持久化执行之外运行的代码暂存不带订阅者的分离根任务启动操作。
    /// </summary>
    /// <remarks>
    /// 启动操作随本 Grain 的下一次日志写入提交，同时提交调用方工作项
    /// 所做的状态变更。本 Grain 上的任务在该次写入后启动。
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
            // 任务先于其记录暂存，因此与任务冲突的启动操作会失败，且不留下记录。
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

        // 已取消的启动方仍会启动任务，其取消不会传播到任务，只会结束自身的订阅。
        if (response is null && IsStartRefusedByAncestorCancellation(recordId, state: null, shielded))
        {
            _pendingHandleResponses[recordId] = StageResponse(recordId, DurableTaskResponse.Canceled);
        }

        return handle;
    }

    /// <summary>清除分离请求的调用方，因为没有调用方认领该请求。</summary>
    private static void PrepareDetachedInvocation(IDurableTaskRequest request)
    {
        var context = IDurableTaskRequest.GetContext(request);
        context.CallerId = default;
        context.SupportsDurableCompletion = false;
    }

    /// <summary>返回任务是否是分离任务的记录，以及它所启动的任务。</summary>
    private static bool TryGetDetachedTask(IDurableTaskState state, out GrainId target, out TaskId rootId)
    {
        target = default;
        rootId = default;
        if (state.RequestFingerprint is not { } fingerprint
            || !fingerprint.StartsWith(DetachedFingerprintPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        // 请求指纹不包含 ':'，位于标识之前。
        var separator = fingerprint.IndexOf(':', DetachedFingerprintPrefix.Length);
        return separator > 0 && TryParseIdentity(fingerprint[..(separator + 1)], fingerprint, out target, out rootId);
    }

    private static string DescribeDetachedStart(GrainId target, TaskId rootId, string? method) =>
        $"a start of detached task '{rootId}' on grain '{target}' for {DescribeMethod(method)}";

    /// <summary>
    /// 返回 Grain 拒绝 <paramref name="taskId"/> 的调用的原因，该调用由发送方首次暂存于
    /// <paramref name="sentAt"/>；若 Grain 接受该调用，则返回 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// 在 <see cref="DurableTaskGrainRuntimeShared.InvocationAdmissionWindow"/> 所规定的时长尚未
    /// 自 <paramref name="sentAt"/> 起经过之前，Grain 会接受调用。发送方与调用一起或在其之后暂存的取消或信号，
    /// 会在到达后保留 <see cref="DurableTaskGrainRuntimeShared.EarlyRecordRetention"/> 指定的时长，
    /// 这涵盖了发送方与 Grain 之间的时钟偏差，所以被接受的调用可以找到该记录。与 Messaging 的
    /// <c>DeliverAt</c> 一样，此检查将发送方的时钟与 Grain 的时钟进行比较。
    /// Grain 已存储的调用的重新投递不会暂存任何新内容，因此 Grain 可在任何时间接受它。
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

    /// <summary>暂存由收件箱投递的调用。</summary>
    /// <param name="taskId">被调用的任务：调用方作用域中的子任务，或分离的根任务。</param>
    /// <param name="request">任务所运行的请求。</param>
    /// <param name="sender">发送调用的 Grain。</param>
    /// <param name="detached">调用是否启动分离任务（该任务没有调用方）。</param>
    /// <param name="subscriberId">订阅分离任务的发送方任务的传输标识符（若有）。</param>
    /// <remarks>
    /// 分离任务的订阅者在存储该任务的同一次写入中添加到任务的完成通知目的地。
    /// 如果调用与已存储的任务冲突（例如相同标识符下的另一请求），
    /// 则在暂存任何内容之前抛出异常，因此接收器以冲突消息和代码拒绝调用，收件箱
    /// 将其记录为已处理。
    /// </remarks>
    internal void InvokeFromInbox(TaskId taskId, IDurableTaskRequest request, GrainId sender, bool detached, TaskId? subscriberId)
    {
        ThrowIfStopping();
        var requestContext = request.Context
            ?? throw new InvalidOperationException("The durable task invocation request has no context.");
        if (detached)
        {
            // 分离任务是根任务，不会由其他 Grain 的调用在保留的作用域根下创建。
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
    /// 通过引用在发件箱中暂存 <paramref name="invocation"/>，并将其记入待处理调用表的日志，
    /// 直到该调用退役；同时记录 <paramref name="recordId"/>，即记录启动结果的任务的传输标识符
    /// （若有）。
    /// </summary>
    /// <remarks>
    /// 标识相同的待处理调用会合并：重放由恢复过程还原的启动操作时，不暂存任何内容。
    /// 记录发起的启动操作的标识包含该记录的传输标识符，因此执行树的每次运行均发送
    /// 自己的消息。待处理调用保留已写入日志的请求，所以参数序列化形式不同的重放
    /// 仍会合并。如果同一标识下的待处理调用调用了不同的方法（由先前投递同一根任务的操作
    /// 发送），则启动操作失败。
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

    /// <summary>返回待处理调用是否与 <paramref name="request"/> 调用了相同的方法。</summary>
    /// <remarks>如果待处理调用的请求无法再解码，则无法证明它调用了相同的方法。</remarks>
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
    /// 在启动 <paramref name="invocation"/> 的记录上记录其退役方式：目标接受了调用，或
    /// 启动失败（该失败使记录结束）；不执行写入。
    /// </summary>
    /// <remarks>
    /// 发件箱在同一次日志写入中暂存对调用的移除操作。已经收到任务结果的记录
    /// 此时已经记录了接受状态。已存储自身结果（例如取消或
    /// 中止）的记录保留该结果，因此每次重放都观察到最先观察到的结果。通过投递启动的根任务没有记录，
    /// 随执行树删除的记录忽略退役通知；由执行树后续运行在相同标识符下
    /// 重建的记录也忽略该通知。
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
    /// 除非 <paramref name="recordId"/>（其传输标识符为 <paramref name="wireId"/>）启动了
    /// <paramref name="sourceTaskId"/> 所标识、位于 <paramref name="sender"/> 上的任务，否则抛出异常：该任务是该 Grain 在
    /// 本 Grain 的调用方作用域下存储的远程子任务，或者该记录订阅了该任务。
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
    /// 等待记录 <paramref name="recordId"/> 持久化记录其启动操作的目标是否接受了
    /// 任务，并抛出目标未接受启动时的结果。
    /// </summary>
    /// <remarks>
    /// 在本 Grain 上的启动操作在其写入提交后即被接受。在其他 Grain 上的启动操作在其调用
    /// 作为已投递退役时，或在任务结果先到达记录时被接受。失败的启动操作以其
    /// 失败结果结束记录，或者以记录先前存储的自身取消或中止结果结束记录。
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

    /// <summary>返回记录是否启动了另一 Grain 上的任务，并等待该 Grain 的接受。</summary>
    private bool AwaitsAcceptance(IDurableTaskState state) =>
        !state.RemoteTarget.IsDefault || (TryGetDetachedTask(state, out var target, out _) && target != GrainId);

    /// <summary>唤醒等待 <paramref name="recordId"/> 的回复的启动操作（若有）。</summary>
    private void SignalAcceptance(TaskId recordId)
    {
        if (_acceptanceWaiters.Remove(recordId, out var waiter))
        {
            waiter.TrySetResult();
        }
    }
}
