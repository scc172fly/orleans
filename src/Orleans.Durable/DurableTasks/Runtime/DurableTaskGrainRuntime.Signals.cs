using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Tasks.Diagnostics;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Durable.Tasks.Runtime;

/// <remarks>
/// <para>
/// 信号寻址到被调用任务，该任务将每个信号名称的第一个值记录为其
/// 执行树中的终态记录，即子记录 <c>$signal:{name}</c>；其指纹为值的指纹，其结果为该
/// 值。记录随投递它的变更一起提交，并随执行树删除，所以执行树中的每次等待，
/// 包括重放期间的等待，都观察到相同的值。值相等的信号是重复信号；若信号
/// 携带不同的值，或者目标任务未由本 Grain 作为被调用任务保留，或者任务已处于终态且无该
/// 记录，则信号被拒绝。收件箱先于调用投递的信号会为该调用保留记录。
/// </para>
/// <para>
/// 等待在记录提交后完成。如果等待被放弃（例如持久化取消或执行树
/// 删除取消了执行实例），则等待会自行移除，因此运行时不会为从未到达的信号保留状态。
/// </para>
/// </remarks>
internal sealed partial class DurableTaskGrainRuntime
{
    // 执行树的记录以 '$' 开头，因此信号记录不会与显式命名的子任务发生冲突。
    private const string SignalSegmentPrefix = "$signal:";
    private const string SignalFingerprintPrefix = "$signal:";
    private const string SignalSendFingerprintPrefix = "$signal-send:";

    // 按信号记录索引的、尚未提交信号的等待操作。
    private readonly Dictionary<TaskId, List<TaskCompletionSource>> _signalWaits = [];

    public async ValueTask<(DurableTaskResponse Value, DateTimeOffset RecordedAt)> WaitForSignalAsync(
        GrainDurableTaskContext attempt,
        TaskId executionId,
        string name,
        CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        var recordId = GetSignalRecordId(GetSignalOwner(executionId), name);
        while (true)
        {
            // 与其他结果一样，暂存的记录会先提交，其等待操作随后才能观察到它。
            if (_storage.TryGetTask(recordId, out var record) && !_pendingHandleResponses.ContainsKey(recordId))
            {
                Debug.Assert(record.CompletedAt.HasValue, "A signal record holds its value.");
                return (ReadRecordedResult(recordId, record)!, record.CompletedAt.Value);
            }

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_signalWaits.TryGetValue(recordId, out var waiters))
            {
                _signalWaits[recordId] = waiters = [];
            }

            waiters.Add(waiter);

            DurableTaskEvents.EmitSignalWaitStarted(GrainContext, recordId);
            try
            {
                await attempt.ParkAsync(waiter.Task.WaitAsync(cancellationToken), [recordId]);
            }
            finally
            {
                if (waiters.Remove(waiter) && waiters.Count == 0)
                {
                    _signalWaits.Remove(recordId);
                }

                DurableTaskEvents.EmitSignalWaitEnded(GrainContext, recordId);
            }
        }
    }

    /// <summary>完成对信号记录的等待，该记录的值已经提交。</summary>
    private void CompleteSignalWaits(TaskId recordId)
    {
        if (_signalWaits.Count == 0 || !_signalWaits.Remove(recordId, out var waiters))
        {
            return;
        }

        foreach (var waiter in waiters)
        {
            waiter.TrySetResult();
        }
    }

    /// <summary>
    /// 从持久化执行实例发送信号，将发送操作记录在 <paramref name="markerId"/> 下，位于该执行实例的
    /// 执行树中，并等待提交两者的日志写入。
    /// </summary>
    /// <param name="throwOnRejection">
    /// <see langword="true"/> 表示本 Grain 上的任务拒绝信号时抛出异常，而不记录发送操作；否则
    /// 标记记录拒绝结果，操作返回该结果，所以重放返回相同的结果。
    /// </param>
    /// <remarks>
    /// 发往本 Grain 的信号直接暂存在任务状态中；发往另一 Grain 的信号暂存在发件箱中，其
    /// 目标的收件箱记录信号值，或将被拒绝的信号移入死信，因此发送操作会报告
    /// <see cref="DurableSignalResult.Accepted"/>。到达标记的重放不会再次发送任何内容，标记随
    /// 执行实例的执行树删除。
    /// </remarks>
    public async ValueTask<DurableSignalResult> SendSignalAsync(
        TaskId markerId,
        GrainId target,
        TaskId taskId,
        string name,
        DurableTaskResponse value,
        bool throwOnRejection,
        CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSignal(taskId, name, value);
        DurableSignalResult result;
        if (_storage.TryGetTask(markerId, out var recorded))
        {
            // 标记的标识符包含目标、任务和信号，因此发送标记始终属于同一操作，
            // 无论重放发送的值为何：即使重放的值以不同形式序列化，也会读取已记录的结果。
            if (recorded.Kind != DurableTaskRecordKind.SignalSend)
            {
                throw CreateDivergenceException(
                    $"Durable task '{markerId}' is already associated with another operation.",
                    recorded,
                    $"sending signal '{name}' to task '{taskId}' on grain '{target}'");
            }

            // 标记随发送操作提交，可能发生在先前的执行尝试仍在等待的写入中。
            await WriteStateAsync(cancellationToken);
            result = ReadRecordedResult(markerId, recorded)!.Result is DurableSignalResult recordedResult ? recordedResult : DurableSignalResult.Accepted;
            if (throwOnRejection && CreateSignalRejection(taskId, name, result) is { } recordedRejection)
            {
                throw recordedRejection;
            }

            return result;
        }

        result = DurableSignalResult.Accepted;
        var payloadFingerprint = GetSignalFingerprint(value);
        if (target == GrainId)
        {
            result = StageSignal(taskId, name, value, payloadFingerprint);
            if (throwOnRejection && CreateSignalRejection(taskId, name, result) is { } rejection)
            {
                throw rejection;
            }
        }
        else
        {
            MessageTransport.SendSignal(GrainId, target, taskId, name, value, payloadFingerprint, GetIncarnation(markerId));
        }

        var marker = GetOrCreateTask(markerId, DurableTaskRecordKind.SignalSend, request: null);
        _storage.SetRequestFingerprint(markerId, marker, GetSignalSendFingerprint(target, taskId, name, payloadFingerprint));
        SetResponse(
            markerId,
            marker,
            result == DurableSignalResult.Accepted ? DurableTaskResponse.Completed : DurableTaskResponse.FromResult(result));
        await WriteStateAsync(cancellationToken);
        return result;
    }

    /// <summary>暂存通过收件箱接收的信号，在收件箱批次提交后完成对它的等待。</summary>
    /// <returns><see langword="null"/> 表示信号已被接受；否则返回拒绝信号的原因。</returns>
    internal Exception? StageSignalFromInbox(TaskId taskId, string name, DurableTaskResponse value)
    {
        ThrowIfStopping();
        ValidateSignal(taskId, name, value);
        return CreateSignalRejection(taskId, name, StageSignal(taskId, name, value, GetSignalFingerprint(value), fromInbox: true));
    }

    async ValueTask IDurableTaskGrainExtension.SignalAsync(
        TaskId taskId,
        string name,
        DurableTaskResponse value,
        CancellationToken cancellationToken) =>
        await SignalOwnTaskAsync(taskId, name, value, throwOnRejection: true, cancellationToken);

    ValueTask<DurableSignalResult> IDurableTaskGrainExtension.TrySignalAsync(
        TaskId taskId,
        string name,
        DurableTaskResponse value,
        CancellationToken cancellationToken) =>
        SignalOwnTaskAsync(taskId, name, value, throwOnRejection: false, cancellationToken);

    async ValueTask IDurableTaskManager.SignalAsync<T>(
        TaskId taskId,
        DurableSignal<T> signal,
        T value,
        CancellationToken cancellationToken) =>
        await SignalFromManagerAsync(taskId, signal, value, throwOnRejection: true, cancellationToken);

    ValueTask<DurableSignalResult> IDurableTaskManager.TrySignalAsync<T>(
        TaskId taskId,
        DurableSignal<T> signal,
        T value,
        CancellationToken cancellationToken) =>
        SignalFromManagerAsync(taskId, signal, value, throwOnRejection: false, cancellationToken);

    private ValueTask<DurableSignalResult> SignalFromManagerAsync<T>(
        TaskId taskId,
        DurableSignal<T> signal,
        T value,
        bool throwOnRejection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var response = DurableTaskResponse.FromResult(value);

        // 持久化执行记录发送操作，因此其重放不依赖目标的当前状态。
        return DurableTaskContext.TryGetCurrent(out var current) && current is GrainDurableTaskContext context
            ? context.SendSignalAsync(GrainId, taskId, signal.Name, response, throwOnRejection, cancellationToken)
            : SignalOwnTaskAsync(taskId, signal.Name, response, throwOnRejection, cancellationToken);
    }

    /// <summary>暂存发往本 Grain 任务的信号，并等待提交该信号的日志写入。</summary>
    /// <remarks>
    /// 重复信号同样等待该次写入，因此操作完成时该值已持久化。拒绝信号不暂存
    /// 任何内容，并根据情况抛出异常或返回拒绝结果。
    /// </remarks>
    private async ValueTask<DurableSignalResult> SignalOwnTaskAsync(
        TaskId taskId,
        string name,
        DurableTaskResponse value,
        bool throwOnRejection,
        CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        ValidateSignal(taskId, name, value);
        var result = StageSignal(taskId, name, value, GetSignalFingerprint(value));
        if (result is DurableSignalResult.Accepted or DurableSignalResult.Duplicate)
        {
            await WriteStateAsync(cancellationToken);
        }
        else if (throwOnRejection)
        {
            throw CreateSignalRejection(taskId, name, result)!;
        }

        return result;
    }

    /// <summary>记录被调用任务的信号的第一个值，但不执行写入。</summary>
    /// <param name="fromInbox">
    /// <see langword="true"/> 表示信号通过收件箱投递，因此可以先于其调用到达。
    /// </param>
    /// <returns>返回信号是否已记录、是否为重复信号，或其被拒绝的原因；拒绝不暂存任何内容。</returns>
    /// <remarks>
    /// 发送方可以在一次写入中提交调用及发往该调用的信号，发件箱分别独立投递两者。
    /// 因此，对于发往未存储任务的信号，或发往仅记录了先于其调用到达的取消请求的任务的信号，
    /// 会先于调用记录；调用随后在自身执行树中找到该记录。该记录
    /// 随执行树删除；若调用始终未到达，则在
    /// <see cref="DurableTaskGrainRuntimeShared.EarlyRecordRetention"/> 指定的时长经过后删除，之后 Grain 不再
    /// 接受发送方先于该信号暂存的调用。
    /// </remarks>
    private DurableSignalResult StageSignal(
        TaskId taskId,
        string name,
        DurableTaskResponse value,
        string payloadFingerprint,
        bool fromInbox = false)
    {
        var recordId = GetSignalRecordId(taskId, name);
        if (_storage.TryGetTask(recordId, out var record))
        {
            return string.Equals(record.RequestFingerprint, payloadFingerprint, StringComparison.Ordinal)
                ? DurableSignalResult.Duplicate
                : DurableSignalResult.Conflict;
        }

        var found = _storage.TryGetTask(taskId, out var task);
        var precedesInvocation = fromInbox && (!found || IsCancellationRecord(task!));
        if (!precedesInvocation)
        {
            if (!found || !IsInvoked(task!))
            {
                return DurableSignalResult.TaskNotFound;
            }

            if (task!.CompletedAt.HasValue)
            {
                return DurableSignalResult.TaskCompleted;
            }
        }

        record = GetOrCreateTask(recordId, DurableTaskRecordKind.SignalValue, request: null);
        _storage.SetRequestFingerprint(recordId, record, payloadFingerprint);
        SetResponse(recordId, record, value);
        _pendingHandleResponses[recordId] = value;
        if (precedesInvocation)
        {
            TrackExpiry(GetEarlySignalExpiry(record));
        }

        return DurableSignalResult.Accepted;
    }

    /// <summary>返回任务是否记录了信号值。</summary>
    private static bool IsSignalRecord(IDurableTaskState state) => state.Kind == DurableTaskRecordKind.SignalValue;

    /// <summary>返回先于调用到达的信号在调用未到达时的删除时间。</summary>
    private DateTimeOffset GetEarlySignalExpiry(IDurableTaskState record) =>
        AddSaturating(record.CreatedAt, _shared.EarlyRecordRetention);

    /// <summary>创建报告信号被拒绝的异常；对于已记录的信号，返回 <see langword="null"/>。</summary>
    private static Exception? CreateSignalRejection(TaskId taskId, string name, DurableSignalResult result) => result switch
    {
        DurableSignalResult.Conflict => new InvalidOperationException(
            $"Durable task '{taskId}' already received a different value for signal '{name}'."),
        DurableSignalResult.TaskNotFound => new DurableTaskNotFoundException(taskId),
        DurableSignalResult.TaskCompleted => new InvalidOperationException(
            $"Durable task '{taskId}' has completed, so it no longer receives signal '{name}'."),
        _ => null,
    };

    private static void ValidateSignal(TaskId taskId, string name, DurableTaskResponse value)
    {
        if (taskId.IsDefault)
        {
            throw new ArgumentException("A signal requires a task identifier.", nameof(taskId));
        }

        ArgumentException.ThrowIfNullOrEmpty(name);
        if (value is not { ResponseKind: DurableTaskResponseKind.CompletedSuccessfully })
        {
            throw new ArgumentException("A signal's value is a successful response.", nameof(value));
        }
    }

    /// <summary>返回执行树包含 <paramref name="executionId"/> 的被调用任务。</summary>
    private TaskId GetSignalOwner(TaskId executionId)
    {
        for (var taskId = executionId; !taskId.IsDefault; taskId = taskId.Parent())
        {
            if (_storage.TryGetTask(taskId, out var state) && IsInvoked(state))
            {
                return taskId;
            }
        }

        throw new InvalidOperationException($"Durable execution '{executionId}' belongs to no invoked task, which receives signals.");
    }

    private static TaskId GetSignalRecordId(TaskId taskId, string name) => taskId.Child(SignalSegmentPrefix + name);

    private string GetSignalFingerprint(DurableTaskResponse value) =>
        SignalFingerprintPrefix + Convert.ToHexString(SHA256.HashData(_shared.Serializer.SerializeToArray(value)));

    private static string GetSignalSendFingerprint(GrainId target, TaskId taskId, string name, string payloadFingerprint)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var value in (ReadOnlySpan<string>)[target.ToString(), taskId.ToString(), name, payloadFingerprint])
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return SignalSendFingerprintPrefix + Convert.ToHexString(hash.GetHashAndReset());
    }
}
