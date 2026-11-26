using System.Runtime.ExceptionServices;

namespace Orleans.Clustering.TestKit;

/// <summary>单独组建的成员提供商及其任择资源所有者。</summary>
public sealed class MembershipTableTestHandle : IAsyncDisposable
{
    private readonly Func<ValueTask>? _dispose;
    private int _disposed;

    /// <summary>创建一个控件。如果有的话, 提供处置者拥有用于此控件的所有资源。</summary>
    public MembershipTableTestHandle(IMembershipTable table, Func<ValueTask>? disposeAsync = null)
    {
        Table = table ?? throw new ArgumentNullException(nameof(table));
        _dispose = disposeAsync;
    }

    /// <summary>获得独立建造的供应商。</summary>
    public IMembershipTable Table { get; }

    /// <summary>最多一次投放自有资源。表格本身没有暗含处理。</summary>
    public ValueTask DisposeAsync() => Interlocked.Exchange(ref _disposed, 1) == 0 && _dispose is not null ? _dispose() : ValueTask.CompletedTask;
}

/// <summary>拥有孤立的集束范围、独立控件、初始化和被捆绑的撕裂。</summary>
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

    /// <summary>创建一个同步工厂的固定装置,接受每个请求的集束编号。</summary>
    public MembershipTableTestFixture(string providerName, Func<string, IMembershipTable> factory,
        Func<string, CancellationToken, ValueTask<bool>> isDeletedAsync, string? serviceId = null)
        : this(providerName, Wrap(factory), isDeletedAsync, serviceId) { }

    /// <summary>创建由明确拥有所有权的不同步集群意识工厂组成的固定装置。</summary>
    public MembershipTableTestFixture(string providerName,
        Func<string, CancellationToken, ValueTask<MembershipTableTestHandle>> factory,
        Func<string, CancellationToken, ValueTask<bool>> isDeletedAsync, string? serviceId = null)
        : this(providerName, Wrap(factory), isDeletedAsync, serviceId) { }

    /// <summary>创建固定符。所有控件都使用相同的服务代号及后端,但所提供的群集代号除外。</summary>
    /// <param name="providerName">诊断中使用的非保密供应商标签。</param>
    /// <param name="factory">创建对所请求范围当前后端的独立拥有的控件。</param>
    /// <param name="isDeletedAsync">
    /// 特定集群原始支持状态的只读本地端探测。 当其成员身份时返回真实位置
    /// 数据被删除,或其所有者已最终取消存储; 当种子数据还存在时返回错误 。
    /// 探测器在所有人处置之前运行,必须保留后端身份,并宣传基础设施故障。
    /// 持久性供应商可以通过独立的后端准入检查同一范围;终端供应商检查
    /// 本地失效。套房检查探测到人口数据后再删除。
    /// </param>
    /// <param name="serviceId">共享服务身份。</param>
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

    /// <summary>获取诊断提供者标签。 不要将证书放在此值中 。</summary>
    public string ProviderName { get; }
    /// <summary>获得通用服务身份</summary>
    public string ServiceId { get; }
    /// <summary>获得第一个孤立的集号ID。</summary>
    public string ClusterId { get; }
    /// <summary>获取第二个孤立的集号ID。</summary>
    public string OtherClusterId { get; }
    /// <summary>启动后获得第一个提供者处理。</summary>
    public IMembershipTable First { get; private set; } = null!;
    /// <summary>获得第一个组群的单独建造提供者。</summary>
    public IMembershipTable Second { get; private set; } = null!;
    /// <summary>获取第二组的提供者。</summary>
    public IMembershipTable OtherCluster { get; private set; } = null!;

    /// <summary>构造并启动三个所需控件。部分故障清理了获得的资源。</summary>
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

    /// <summary>在其中的一个固定线的自有范围中获取另一个控件。 初始化是明确的。</summary>
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
        // 请求失败后可以执行。 在本地证据证明保留之前, 将这些手柄退掉 。
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

    /// <summary>运行一个完全孤立的个案,如果拆卸失败则保留其主要失败。</summary>
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

    /// <summary>清理工作最多要等30秒。所有者在清理工作完成之前,仍活着。</summary>
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
            // 不可撤销的发送装置保留无一例外兼容供应商的实际完成时间。
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
