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
/// 信号表示援引的任务,该任务记录每个信号名称的第一个值,作为在
/// 执行树: 孩子 <c>$signal:{name}</c>,其指纹是值的指纹,结果就是
/// 值。记录承诺与交付该记录的更改一起进行,并随树删除,所以在树上每等一等,
/// 包含重放中的等待, 观察相同值。 等值的信号是重复的, 和
/// 具有不同值的数值,或用于本任务 grain 不得保留被援引的或没有它就属于终点
/// 拒绝, 输入信箱在援引前发送的信号被记录为该引用。
/// </para>
/// <para>
/// 记录一旦完成即完成等待。一个被废弃的等待,例如由于耐久取消或树
/// 取消执行, 自行删除, 所以运行时间不会保留任何信号状态, 因为信号从未到达 。
/// </para>
/// </remarks>
internal sealed partial class DurableTaskGrainRuntime
{
    // 执行树记录从"$"开始 所以信号记录不会与 明确命名的孩子相撞
    private const string SignalSegmentPrefix = "$signal:";
    private const string SignalFingerprintPrefix = "$signal:";
    private const string SignalSendFingerprintPrefix = "$signal-send:";

    // 等待尚未承诺的信号 通过信号记录
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
            // 象其他结果一样,在等待到来之前就作一个有阶段记录的记录。
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

    /// <summary>完成一个已承诺值的信号记录的等待。</summary>
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
    /// 执行中从耐久处决发出信号,在处决时记录 <paramref name="markerId"/> 之下的发送
    /// 树,等待日记写作,两者兼具。
    /// </summary>
    /// <param name="throwOnRejection">
    /// <see langword="true"/> 投键 grain任务拒绝信号,而不记录发送;否则
    /// 标记拒绝, 操作返回后, 重放返回相同的结果 。
    /// </param>
    /// <remarks>
    /// 此 grain 的信号直接在任务状态下发出;一个对另一个对另一个对另一个对 grain 的信号放在发件箱中,该发件箱
    /// 目标的收件箱记录值或死字母, 拒绝信号, 所以发送报告
    /// <see cref="DurableSignalResult.Accepted"/>。一个重放,到达标记的重放,再发出一无任何信号,而标记就是
    /// 将执行树删除。
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
            // 标记标记的标识符识别符号指定目标、任务和信号, 所以发送的标记就是同一个操作
            // 重播发送的值: 一个其序列价值不同地重复读取记录的结果。
            if (recorded.Kind != DurableTaskRecordKind.SignalSend)
            {
                throw CreateDivergenceException(
                    $"Durable task '{markerId}' is already associated with another operation.",
                    recorded,
                    $"sending signal '{name}' to task '{taskId}' on grain '{target}'");
            }

            // 标记与发送承诺,可能写在早期尝试仍在等待的一封信中。
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

    /// <summary>阶段通过收件箱接收信号,完成收件箱批次承诺后等待。</summary>
    /// <returns><see langword="null"/> 当信号被接受或被拒绝时。</returns>
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

        // 耐久执行记录发送, 所以它的重播不取决于目标的当前状态。
        return DurableTaskContext.TryGetCurrent(out var current) && current is GrainDurableTaskContext context
            ? context.SendSignalAsync(GrainId, taskId, signal.Name, response, throwOnRejection, cancellationToken)
            : SignalOwnTaskAsync(taskId, signal.Name, response, throwOnRejection, cancellationToken);
    }

    /// 阶段<summary>, 即此 grain 任务的一个信号, 等待日记的写作。</summary>
    /// <remarks>
    /// 重复还等待写入, 所以当操作完成时值是可持久的。 拒绝阶段
    /// 空无所物,不抛下,不退回去。
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

    /// <summary>记录所援引任务信号的第一个值,不以书面形式。</summary>
    /// <param name="fromInbox">
    /// <see langword="true"/> 当信号通过信箱传送时, 以便它能在请求之前发出。
    /// </param>
    /// <returns>是否记录了信号,是重复的,或为什么拒绝;拒绝阶段没有。</returns>
    /// <remarks>
    /// 发送者可以进行一个呼吁和发出一个信号,用一个字来表示,发件人独立地发送。
    /// 发送信号, 用于一项任务, 而不存储, 或只记录取消之前的
    /// 因此,在援引之前记录了援引,而该援引在其树上发现。
    /// 或,当无任何祈祷到达时,一旦
    /// <see cref="DurableTaskGrainRuntimeShared.EarlyRecordRetention"/> 已过期,此后 grain 不再
    /// 承认发信人在信号前作出的援引。
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

    /// <summary>返回任务是否记录信号值。</summary>
    private static bool IsSignalRecord(IDurableTaskState state) => state.Kind == DurableTaskRecordKind.SignalValue;

    /// <summary>返回,当信号在被援引之前发出的信号被删除时,除非该信号到达。</summary>
    private DateTimeOffset GetEarlySignalExpiry(IDurableTaskState record) =>
        AddSaturating(record.CreatedAt, _shared.EarlyRecordRetention);

    /// <summary>创建例外,报告拒绝信号或返回所记录的 <see langword="null"/> 。</summary>
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

    /// <summary>返回所引用的任务,其执行树含有 <paramref name="executionId"/>。</summary>
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
