using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Jobs;
using Orleans.Durable.Journaling;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Durable.Tasks.Storage;

/// <summary>
/// 储存 grain日记中的耐久任务状态。
/// </summary>
/// <remarks>
/// <para>
/// 每次更新都重新写入任务状态, 因此请求和结果有效载荷将分别进行日记: 每一个有效载荷
/// 回收状态的有效载荷附于
/// 第一次储存暴露它。
/// </para>
/// <para>
/// 任务等级以内存为索引。该索引是根据第一次使用时回收的任务构建的,然后加以维护
/// 当任务被创建和删除时; 日记每激活一次恢复一次, 在运行时间使用存储前 。
/// </para>
/// <para>
/// 每个存储的结果都标记为下一个完成序列。 最后的顺序没有日记: 它是导出
/// 首期使用时回收的任务, 作为其最高顺序, 所以印章不花费自己日记的输入。
/// 删除终端执行树可降低衍生值,但只比较序列的组合器
/// 比较运行中执行的任务,这些任务仍然保存。
/// </para>
/// </remarks>
internal sealed class DurableTaskGrainStorage : IDurableTaskGrainStorage
{
    private readonly IDurableDictionary<TaskId, DurableTaskState> _items;
    private readonly IDurableDictionary<TaskId, byte[]> _requests;
    private readonly IDurableDictionary<TaskId, byte[]> _results;
    private readonly IJournaledStateManager _stateManager;
    private readonly TimeProvider _timeProvider;
    private readonly Serializer _serializer;
    private readonly Func<TaskId, bool> _isStored;
    private DurableTaskHierarchy? _hierarchy;
    private long? _lastCompletionSequence;

    public DurableTaskGrainStorage(
        [FromKeyedServices("$tasks")] IDurableDictionary<TaskId, DurableTaskState> items,
        [FromKeyedServices("$task-requests")] IDurableDictionary<TaskId, byte[]> requests,
        [FromKeyedServices("$task-results")] IDurableDictionary<TaskId, byte[]> results,
        IJournaledStateManager stateManager,
        [FromKeyedServices(DurableJobTimeProviderNames.DurableJobs)] TimeProvider timeProvider,
        Serializer serializer)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(stateManager);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(serializer);

        _items = items;
        _requests = requests;
        _results = results;
        _stateManager = stateManager;
        _timeProvider = timeProvider;
        _serializer = serializer;
        _isStored = items.ContainsKey;
    }

    public IEnumerable<(TaskId Id, IDurableTaskState State)> Tasks =>
        _items.Select(pair => (pair.Key, (IDurableTaskState)Expose(pair.Key, pair.Value)));

    public IEnumerable<(TaskId Id, IDurableTaskState State)> GetChildren(TaskId parentId) =>
        (_hierarchy ??= DurableTaskHierarchy.Create(_items.Keys))
            .GetNearestStoredDescendants(parentId, _isStored)
            .Select(id => (id, (IDurableTaskState)Expose(id, _items[id])));

    public IDurableTaskState GetOrCreateTask(TaskId taskId, DurableTaskRecordKind kind, IDurableTaskRequest? request)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(taskId, default);
        if (_items.TryGetValue(taskId, out var result))
        {
            return Expose(taskId, result);
        }

        if (request is not null)
        {
            ValidateRequest(request);
        }

        result = new DurableTaskState { Kind = kind, CreatedAt = _timeProvider.GetUtcNow() }.Bind(_serializer);
        result.SetRequest(request);
        _items.Add(taskId, result);
        _hierarchy?.Add(taskId);
        if (result.RequestPayload is { } payload)
        {
            _requests[taskId] = payload;
        }

        return result;
    }

    public void SetRequest(TaskId taskId, IDurableTaskState state, IDurableTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var typedState = GetState(taskId, state);
        typedState.SetRequest(request);
        _requests[taskId] = typedState.RequestPayload!;
    }

    public void SetRequestFingerprint(TaskId taskId, IDurableTaskState state, string fingerprint, string? method = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        var typedState = GetState(taskId, state);
        typedState.RequestFingerprint = fingerprint;
        if (method is not null)
        {
            typedState.Method = method;
        }

        _items[taskId] = typedState;
    }

    public void SetRemoteRequest(TaskId taskId, IDurableTaskState state, GrainId target, string method, string fingerprint)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(target, default);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        var typedState = GetState(taskId, state);
        typedState.RemoteTarget = target;
        typedState.Method = method;
        typedState.RemoteRequestFingerprint = fingerprint;
        _items[taskId] = typedState;
    }

    public void SetCallerId(TaskId taskId, IDurableTaskState state, GrainId callerId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(callerId, default);
        var typedState = GetState(taskId, state);
        typedState.CallerId = callerId;
        _items[taskId] = typedState;
    }

    public void SetInvocation(TaskId taskId, IDurableTaskState state)
    {
        var typedState = GetState(taskId, state);
        Debug.Assert(typedState.Kind == DurableTaskRecordKind.EarlyCancellation, "Only an early cancellation record becomes its invocation.");
        typedState.Kind = DurableTaskRecordKind.Invocation;
        _items[taskId] = typedState;
    }

    public void SetIncarnation(TaskId taskId, IDurableTaskState state, Guid incarnation)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(incarnation, Guid.Empty);
        var typedState = GetState(taskId, state);
        typedState.Incarnation = incarnation;
        _items[taskId] = typedState;
    }

    public void SetResponse(TaskId taskId, IDurableTaskState state, DurableTaskResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var typedState = GetState(taskId, state);
        typedState.SetResult(response);
        typedState.CompletedAt = _timeProvider.GetUtcNow();
        typedState.CompletionSequence = NextCompletionSequence();
        typedState.DueTime = null;
        if (typedState.ResumeGeneration > 0)
        {
            typedState.ResumeGeneration = checked(typedState.ResumeGeneration + 1);
        }
        _items[taskId] = typedState;
        _results[taskId] = typedState.ResultPayload!;
    }

    /// <remarks>
    /// 记录在添加前已经完整, 因此日记有一个国家条目和一个国家条目
    /// 有效载荷。结果不会在任何事情发生之前, 将抛出物序列化 。
    /// </remarks>
    public IDurableTaskState AddRecord(TaskId taskId, DurableTaskRecordKind kind, string fingerprint, DurableTaskResponse response)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(taskId, default);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentNullException.ThrowIfNull(response);
        Debug.Assert(!_items.ContainsKey(taskId), "A record is added once.");
        var now = _timeProvider.GetUtcNow();
        var state = new DurableTaskState { Kind = kind, CreatedAt = now, RequestFingerprint = fingerprint }.Bind(_serializer);
        state.SetRequest(null);
        state.SetResult(response);
        state.CompletedAt = now;
        state.CompletionSequence = NextCompletionSequence();
        _items.Add(taskId, state);
        _hierarchy?.Add(taskId);
        _results[taskId] = state.ResultPayload!;
        return state;
    }

    private long NextCompletionSequence()
    {
        if (_lastCompletionSequence is not { } last)
        {
            last = 0;
            foreach (var (_, state) in _items)
            {
                last = Math.Max(last, state.CompletionSequence);
            }
        }

        _lastCompletionSequence = ++last;
        return last;
    }

    public void RequestCancellation(TaskId taskId, IDurableTaskState state, string? reason = null)
    {
        var typedState = GetState(taskId, state);
        if (typedState.CancellationRequestedAt.HasValue || typedState.CompletedAt.HasValue)
        {
            return;
        }

        typedState.CancellationRequestedAt = _timeProvider.GetUtcNow();
        typedState.CancellationReason = reason;
        _items[taskId] = typedState;
    }

    public void Shield(TaskId taskId, IDurableTaskState state)
    {
        var typedState = GetState(taskId, state);
        typedState.Shielded = true;
        _items[taskId] = typedState;
    }

    public void SetAccepted(TaskId taskId, IDurableTaskState state, bool accepted)
    {
        var typedState = GetState(taskId, state);
        typedState.Accepted = accepted;
        _items[taskId] = typedState;
    }

    public void SetDelay(TaskId taskId, IDurableTaskState state, DateTimeOffset dueTime, long generation)
    {
        var typedState = GetState(taskId, state);
        typedState.DueTime = dueTime;
        typedState.ResumeGeneration = generation;
        _items[taskId] = typedState;
    }

    public void AddCompletionDestination(TaskId taskId, IDurableTaskState state, DurableTaskSubscriber destination)
    {
        var typedState = GetState(taskId, state);
        if (typedState.CompletionDestinations.Add(destination))
        {
            _items[taskId] = typedState;
        }
    }

    public void RemoveCompletionDestination(TaskId taskId, IDurableTaskState state, DurableTaskSubscriber destination)
    {
        var typedState = GetState(taskId, state);
        if (typedState.CompletionDestinations.Remove(destination))
        {
            _items[taskId] = typedState;
        }
    }

    public void RetainResult(TaskId taskId, IDurableTaskState state)
    {
        var typedState = GetState(taskId, state);
        typedState.RetainedSince = _timeProvider.GetUtcNow();
        _items[taskId] = typedState;
    }

    public bool TryGetTask(TaskId taskId, [NotNullWhen(true)] out IDurableTaskState? state)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(taskId, default);
        if (_items.TryGetValue(taskId, out var result))
        {
            state = Expose(taskId, result);
            return true;
        }

        state = null;
        return false;
    }

    public bool RemoveTask(TaskId taskId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(taskId, default);
        Debug.Assert(
            !_items.TryGetValue(taskId, out var removed) || removed.CompletionDestinations.Count == 0,
            "A pending completion retains its task's result.");
        _requests.Remove(taskId);
        _results.Remove(taskId);
        if (!_items.Remove(taskId))
        {
            return false;
        }

        _hierarchy?.Remove(taskId, _isStored);
        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _requests.Clear();
        _results.Clear();
        _hierarchy = null;
        _lastCompletionSequence = null;
    }

    public ValueTask WriteAsync(CancellationToken cancellationToken) =>
        _stateManager.WriteStateAsync(cancellationToken);

    public ValueTask ReadAsync(CancellationToken cancellationToken)
    {
        _hierarchy = null;
        _lastCompletionSequence = null;
        return _stateManager.InitializeAsync(cancellationToken);
    }

    private static void ValidateRequest(IDurableTaskRequest request)
    {
        if (request.Context is null)
        {
            throw new InvalidOperationException("The request context must not be null.");
        }
    }

    private DurableTaskState Expose(TaskId taskId, DurableTaskState state)
    {
        state.Bind(_serializer);
        if (!state.PayloadsAttached)
        {
            state.AttachPayloads(
                _requests.TryGetValue(taskId, out var request) ? request : null,
                _results.TryGetValue(taskId, out var result) ? result : null);
        }

        return state;
    }

    private DurableTaskState GetState(TaskId taskId, IDurableTaskState state)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(taskId, default);
        ArgumentNullException.ThrowIfNull(state);

        if (state is not DurableTaskState result
            || !_items.TryGetValue(taskId, out var current)
            || !ReferenceEquals(current, result))
        {
            throw new ArgumentException("The provided value does not belong to this storage provider.", nameof(state));
        }

        return result;
    }
}
