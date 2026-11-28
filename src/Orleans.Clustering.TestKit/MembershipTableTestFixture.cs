using System.Runtime.ExceptionServices;

namespace Orleans.Clustering.TestKit;

/// <summary>一个单独构造的成员资格提供程序及其可选资源所有者。</summary>
public sealed class MembershipTableTestHandle : IAsyncDisposable
{
    private readonly Func<ValueTask>? _dispose;
    private int _disposed;

    /// <summary>创建句柄。提供的释放器（如果有）拥有此句柄的全部资源。</summary>
    public MembershipTableTestHandle(IMembershipTable table, Func<ValueTask>? disposeAsync = null)
    {
        Table = table ?? throw new ArgumentNullException(nameof(table));
        _dispose = disposeAsync;
    }

    /// <summary>获取独立构造的提供程序。</summary>
    public IMembershipTable Table { get; }

    /// <summary>最多释放一次所拥有的资源。不隐式释放成员资格表本身。</summary>
    public ValueTask DisposeAsync() => Interlocked.Exchange(ref _disposed, 1) == 0 && _dispose is not null ? _dispose() : ValueTask.CompletedTask;
}

/// <summary>拥有隔离的集群作用域和独立句柄，并负责初始化及有界清理。</summary>
public sealed class MembershipTableTestFixture : IAsyncDisposable
{
    private readonly Func<string, string, CancellationToken, ValueTask<MembershipTableTestHandle>> _factory;
    private readonly Func<string, CancellationToken, ValueTask<bool>> _isDeleted;
    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly List<MembershipTableTestHandle> _handles = [];
    private readonly Dictionary<string, IMembershipTable> _clusters = new(StringComparer.Ordinal);
    private readonly HashSet<string> _endedClusters = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _pendingOperations = [];
    private readonly List<Exception> _operationFailures = [];
    private bool _initialized;
    private int _disposed;
    private Task? _disposalTask;

    /// <summary>根据接收每个所请求集群 ID 的同步工厂创建测试夹具。</summary>
    public MembershipTableTestFixture(string providerName, Func<string, IMembershipTable> factory,
        Func<string, CancellationToken, ValueTask<bool>> isDeletedAsync, string? serviceId = null)
        : this(providerName, Wrap(factory), isDeletedAsync, serviceId) { }

    /// <summary>根据感知集群且明确资源所有权的异步工厂创建测试夹具。</summary>
    public MembershipTableTestFixture(string providerName,
        Func<string, CancellationToken, ValueTask<MembershipTableTestHandle>> factory,
        Func<string, CancellationToken, ValueTask<bool>> isDeletedAsync, string? serviceId = null)
        : this(providerName, Wrap(factory), isDeletedAsync, serviceId) { }

    /// <summary>创建测试夹具。所有句柄使用相同的服务 ID 和后端，但使用各自传入的集群 ID。</summary>
    /// <param name="providerName">用于诊断的不含机密信息的提供程序标签。</param>
    /// <param name="factory">基于请求作用域的当前后端创建独立拥有的句柄。</param>
    /// <param name="isDeletedAsync">
    /// 对指定集群的原始后端状态进行只读原生探测。当其成员资格
    /// 数据被删除或其所有者已使存储永久失效时返回 true；预置数据仍然存在时返回 false。
    /// 探测在释放所有者前运行，必须保持后端标识不变，并且必须传播基础设施故障。
    /// 持久化提供程序可通过独立后端访问检查同一作用域；具有终止语义的提供程序则检查
    /// 原生失效状态。测试套件在调用删除操作之前，会针对已填充的数据验证探测结果。
    /// </param>
    /// <param name="serviceId">共享的服务 ID。</param>
    public MembershipTableTestFixture(string providerName,
        Func<string, string, CancellationToken, ValueTask<MembershipTableTestHandle>> factory,
        Func<string, CancellationToken, ValueTask<bool>> isDeletedAsync, string? serviceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _isDeleted = isDeletedAsync ?? throw new ArgumentNullException(nameof(isDeletedAsync));
        ProviderName = providerName;
        ServiceId = serviceId ?? "clustering-testkit";
        ArgumentException.ThrowIfNullOrWhiteSpace(ServiceId);
        var suffix = Guid.NewGuid().ToString("N");
        ClusterId = $"ctk-a-{suffix}";
        OtherClusterId = $"{ClusterId}-other";
    }

    /// <summary>获取用于诊断的提供程序标签。不要将凭据放入此值。</summary>
    public string ProviderName { get; }
    /// <summary>获取公共服务 ID。</summary>
    public string ServiceId { get; }
    /// <summary>获取第一个隔离集群的 ID。</summary>
    public string ClusterId { get; }
    /// <summary>获取第二个隔离集群的 ID。</summary>
    public string OtherClusterId { get; }
    /// <summary>获取初始化后的第一个提供程序句柄。</summary>
    public IMembershipTable First { get; private set; } = null!;
    /// <summary>获取为第一个集群单独构造的提供程序。</summary>
    public IMembershipTable Second { get; private set; } = null!;
    /// <summary>获取第二个集群的提供程序。</summary>
    public IMembershipTable OtherCluster { get; private set; } = null!;

    /// <summary>构造并初始化三个必需句柄。部分失败时清理已获取的资源。</summary>
    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            lock (_lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                if (_endedClusters.Count > 0)
                    throw new InvalidOperationException("A deleted history requires a new fixture and owner.");
            }
            try { await InitializeCoreAsync(cancellationToken); }
            catch (Exception primary)
            {
                try { await DisposeAsync(cancellationToken.IsCancellationRequested ? TimeSpan.Zero : TimeSpan.FromSeconds(30)); }
                catch (Exception cleanup) { ClusteringTestKitDiagnostics.AttachCleanupFailure(primary, cleanup); }
                throw;
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async ValueTask InitializeCoreAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource pending;
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_endedClusters.Count > 0)
                throw new InvalidOperationException("A deleted history requires a new fixture and owner.");
            if (_initialized) return;
            pending = BeginOperation();
        }
        try
        {
            First = (await CreateAdditionalHandleAsync(ClusterId, cancellationToken)).Table;
            Second = (await CreateAdditionalHandleAsync(ClusterId, cancellationToken)).Table;
            OtherCluster = (await CreateAdditionalHandleAsync(OtherClusterId, cancellationToken)).Table;
            foreach (var table in new[] { First, Second, OtherCluster })
            {
                var operation = InitializeTableAsync(table);
                try { await operation.WaitAsync(cancellationToken); }
                catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
                {
                    _ = ObserveLateCleanupFailureAsync(operation, exception);
                    throw;
                }
            }
            lock (_lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                _initialized = true;
            }
        }
        finally
        {
            EndOperation(pending);
        }
    }

    private async Task InitializeTableAsync(IMembershipTable table)
    {
        TaskCompletionSource pending;
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            pending = BeginOperation();
        }
        try { await table.InitializeMembershipTableAsync(true, CancellationToken.None); }
        catch (Exception exception)
        {
            lock (_lifecycleLock) _operationFailures.Add(exception);
            throw;
        }
        finally { EndOperation(pending); }
    }

    /// <summary>在此测试夹具所拥有的一个作用域中获取另一个句柄。初始化需显式进行。</summary>
    public async ValueTask<MembershipTableTestHandle> CreateAdditionalHandleAsync(string clusterId, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource pending;
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (clusterId != ClusterId && clusterId != OtherClusterId)
                throw new ArgumentException("Additional handles must belong to a fixture-owned cluster.", nameof(clusterId));
            if (_endedClusters.Contains(clusterId))
                throw new InvalidOperationException("A deleted history requires a new fixture and owner.");
            cancellationToken.ThrowIfCancellationRequested();
            pending = BeginOperation();
        }
        var operation = CreateAdditionalHandleCoreAsync(clusterId, pending);
        try { return await operation.WaitAsync(cancellationToken); }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            _ = ObserveLateCleanupFailureAsync(operation, exception);
            throw;
        }
    }

    private async Task<MembershipTableTestHandle> CreateAdditionalHandleCoreAsync(string clusterId, TaskCompletionSource pending)
    {
        try
        {
            MembershipTableTestHandle handle;
            try
            {
                handle = await _factory(ServiceId, clusterId, CancellationToken.None)
                    ?? throw new InvalidOperationException("The membership handle factory returned null.");
            }
            catch (Exception exception)
            {
                lock (_lifecycleLock) _operationFailures.Add(exception);
                throw;
            }
            bool disposed;
            ClusteringConformanceException? duplicateFailure = null;
            lock (_lifecycleLock)
            {
                disposed = _disposed != 0;
                if (_handles.Contains(handle))
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    if (_endedClusters.Contains(clusterId))
                        throw new InvalidOperationException("The factory completed after its cluster history ended.");
                    throw new ClusteringConformanceException(
                        $"provider={ProviderName}; cluster={clusterId}; factory returned the same provider instance; independently construct each IMembershipTable");
                }
                if (!disposed && !_endedClusters.Contains(clusterId))
                {
                    var duplicate = _handles.Any(h => ReferenceEquals(h.Table, handle.Table));
                    if (!duplicate)
                    {
                        _handles.Add(handle);
                        _clusters.TryAdd(clusterId, handle.Table);
                        return handle;
                    }
                    duplicateFailure = new ClusteringConformanceException(
                        $"provider={ProviderName}; cluster={clusterId}; factory returned the same provider instance; independently construct each IMembershipTable");
                }
            }

            try { await handle.DisposeAsync(); }
            catch (Exception exception)
            {
                lock (_lifecycleLock) _operationFailures.Add(exception);
                if (duplicateFailure is not null)
                {
                    ClusteringTestKitDiagnostics.AttachCleanupFailure(duplicateFailure, exception);
                    throw duplicateFailure;
                }
                throw;
            }
            if (duplicateFailure is not null) throw duplicateFailure;
            if (disposed)
                throw new ObjectDisposedException(nameof(MembershipTableTestFixture), "The factory completed after its fixture was disposed.");
            throw new InvalidOperationException("The factory completed after its cluster history ended.");
        }
        finally
        {
            EndOperation(pending);
        }
    }

    private TaskCompletionSource BeginOperation()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingOperations.Add(pending.Task);
        return pending;
    }

    private void EndOperation(TaskCompletionSource pending)
    {
        lock (_lifecycleLock)
        {
            _pendingOperations.Remove(pending.Task);
            pending.SetResult();
        }
    }

    internal async Task AssertHistoryPresentAsync(string clusterId, CancellationToken cancellationToken)
    {
        ClusteringTestKitDiagnostics.Require(!await ObserveDeletionAsync(clusterId, cancellationToken),
            $"deletion probe reported deleted populated history: cluster={clusterId}");
    }

    internal async Task<bool> DeleteClusterAsync(IMembershipTable table, string clusterId, bool allowRetained, CancellationToken cancellationToken)
    {
        TaskCompletionSource pending;
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            pending = BeginOperation();
        }
        var operation = DeleteClusterCoreAsync(table, clusterId, allowRetained, pending);
        try { return await operation.WaitAsync(cancellationToken); }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            _ = ObserveLateCleanupFailureAsync(operation, exception);
            throw;
        }
    }

    private async Task<bool> DeleteClusterCoreAsync(IMembershipTable table, string clusterId, bool allowRetained, TaskCompletionSource pending)
    {
        try { return await DeleteAndVerifyAsync(table, clusterId, allowRetained); }
        catch (Exception exception)
        {
            lock (_lifecycleLock) _operationFailures.Add(exception);
            throw;
        }
        finally { EndOperation(pending); }
    }

    private async Task<bool> DeleteAndVerifyAsync(IMembershipTable table, string clusterId, bool allowRetained)
    {
        ClusteringTestKitDiagnostics.Require(!await ProbeDeletionAsync(clusterId),
            $"deletion probe reported deleted populated history: cluster={clusterId}");
        // 失败的请求也可能已经提交。停止使用这些句柄，直至原生证据证实数据仍被保留。
        lock (_lifecycleLock) _endedClusters.Add(clusterId);
        var rejected = false;
        try
        {
            await table.DeleteMembershipTableEntriesAsync(clusterId, CancellationToken.None);
        }
        catch (ArgumentException exception) when (allowRetained && exception.ParamName == nameof(clusterId))
        {
            rejected = true;
        }

        var deleted = await ProbeDeletionAsync(clusterId);
        ClusteringTestKitDiagnostics.Require(!rejected || !deleted,
            $"rejected foreign deletion changed its target scope: cluster={clusterId}");
        ClusteringTestKitDiagnostics.Require(allowRetained || deleted,
            $"deletion left populated history: cluster={clusterId}");
        return deleted;
    }

    private async ValueTask<bool> ObserveDeletionAsync(string clusterId, CancellationToken cancellationToken)
    {
        TaskCompletionSource pending;
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            pending = BeginOperation();
        }
        var operation = ObserveDeletionCoreAsync(clusterId, pending);
        try { return await operation.WaitAsync(cancellationToken); }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            _ = ObserveLateCleanupFailureAsync(operation, exception);
            throw;
        }
    }

    private async Task<bool> ObserveDeletionCoreAsync(string clusterId, TaskCompletionSource pending)
    {
        try { return await ProbeDeletionAsync(clusterId); }
        catch (Exception exception)
        {
            lock (_lifecycleLock) _operationFailures.Add(exception);
            throw;
        }
        finally { EndOperation(pending); }
    }

    private async Task<bool> ProbeDeletionAsync(string clusterId)
    {
        lock (_lifecycleLock) _endedClusters.Add(clusterId);
        var deleted = await _isDeleted(clusterId, CancellationToken.None);
        if (!deleted)
        {
            lock (_lifecycleLock) _endedClusters.Remove(clusterId);
        }
        return deleted;
    }

    /// <summary>运行一个完整的隔离用例；若清理也失败，则保留该用例的主要失败原因。</summary>
    public async Task RunAsync(Func<MembershipTableTestFixture, CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        Exception? primary = null;
        try
        {
            await InitializeAsync(cancellationToken);
            await action(this, cancellationToken);
        }
        catch (Exception exception) { primary = exception; }
        try { await DisposeAsync(); }
        catch (Exception cleanup)
        {
            if (primary is null) primary = cleanup;
            else ClusteringTestKitDiagnostics.AttachCleanupFailure(primary, cleanup);
        }

        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    /// <summary>最多等待 30 秒进行清理。所有者保持存活，直至其清理操作完成。</summary>
    public ValueTask DisposeAsync() => DisposeAsync(TimeSpan.FromSeconds(30));

    internal async ValueTask DisposeAsync(TimeSpan waitTimeout)
    {
        Task completion;
        TaskCompletionSource? started = null;
        Task[] pending = [];
        lock (_lifecycleLock)
        {
            if (_disposalTask is null)
            {
                _disposed = 1;
                pending = _pendingOperations.ToArray();
                started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposalTask = started.Task;
            }
            completion = _disposalTask;
        }

        if (started is not null) _ = Task.Run(() => CompleteDisposalAsync(pending, started));
        try
        {
            await completion.WaitAsync(waitTimeout);
        }
        catch (TimeoutException exception) when (!completion.IsCompleted)
        {
            exception.Data[ClusteringTestKitDiagnostics.CleanupCompletionKey] = completion;
            _ = ObserveLateCleanupFailureAsync(completion, exception);
            throw;
        }
    }

    private async Task CompleteDisposalAsync(Task[] pending, TaskCompletionSource completion)
    {
        await Task.WhenAll(pending);
        KeyValuePair<string, IMembershipTable>[] clusters;
        MembershipTableTestHandle[] handles;
        lock (_lifecycleLock)
        {
            clusters = _clusters.Where(pair => !_endedClusters.Contains(pair.Key)).ToArray();
            handles = _handles.ToArray();
        }
        List<Exception> failures;
        lock (_lifecycleLock) failures = [.. _operationFailures];
        foreach (var (cluster, table) in clusters)
        {
            // 对于不支持取消令牌的兼容提供程序，不可取消的派发会保留实际完成语义。
            try { await table.DeleteMembershipTableEntriesAsync(cluster, CancellationToken.None); }
            catch (Exception exception) { failures.Add(new InvalidOperationException($"cleanup cluster={cluster}", exception)); }
        }

        foreach (var handle in handles.AsEnumerable().Reverse())
        {
            try { await handle.DisposeAsync(); }
            catch (Exception exception) { failures.Add(exception); }
        }

        if (failures.Count > 0) completion.SetException(new AggregateException("Membership fixture teardown failed.", failures));
        else completion.SetResult();
    }

    private static async Task ObserveLateCleanupFailureAsync(Task completion, Exception timeout)
    {
        try { await completion; }
        catch (Exception failure) { ClusteringTestKitDiagnostics.AttachCleanupFailure(timeout, failure); }
    }

    private static Func<string, string, CancellationToken, ValueTask<MembershipTableTestHandle>> Wrap(Func<string, IMembershipTable> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return (_, cluster, _) => ValueTask.FromResult(new MembershipTableTestHandle(factory(cluster)));
    }

    private static Func<string, string, CancellationToken, ValueTask<MembershipTableTestHandle>> Wrap(
        Func<string, CancellationToken, ValueTask<MembershipTableTestHandle>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return (_, cluster, token) => factory(cluster, token);
    }
}
