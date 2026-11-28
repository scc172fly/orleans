using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.CodeGeneration;
using Orleans.Configuration;
using Orleans.Diagnostics;
using Orleans.Internal;
using Orleans.Metadata;
using Orleans.Runtime.Internal;
using Orleans.Runtime.Messaging;
using Orleans.Runtime.Scheduler;

namespace Orleans.Runtime;

/// <summary>
/// 将 Grain 激活实例迁移到 Silo 的远程接口。
/// </summary>
/// <remarks>
/// 版本 1 支持在迁移 Grain 的同时接受排队请求，并添加 <see cref="RetargetMigratedRequests"/>。
/// Silo 只会向清单中声明此接口版本为 1 或更高的 Silo 发送排队请求。
/// </remarks>
[Version(ActivationMigrationManager.MigratedRequestsVersion)]
internal interface IActivationMigrationManagerSystemTarget : ISystemTarget
{
    /// <summary>
    /// 尽力接受迁移中的 Grain 以及此前为其排队的请求。
    /// </summary>
    /// <param name="migratingGrains">迁移中的 Grain。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 此 Silo 接受调用时取得请求的所有权。激活实例通过普通 FIFO 准入流程接收它们。
    /// 新激活实例在注册到 Grain 目录之前接收请求。
    /// </remarks>
    [Alias("29E9E63F")]
    ValueTask AcceptMigratingGrains(
        [Immutable] List<GrainMigrationPackage> migratingGrains,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 将此 Silo 上的待完成调用重新指向接受其请求的 Silo。
    /// </summary>
    /// <param name="routes">需要重定向的调用。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    [Alias("RetargetMigratedRequests")]
    ValueTask RetargetMigratedRequests(
        [Immutable] List<MigratedRequestRoute> routes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 标识一个待完成调用及当前拥有其请求的 Silo。
/// </summary>
[GenerateSerializer, Immutable]
internal readonly record struct MigratedRequestRoute(
    [property: Id(0)] GrainId SendingGrain,
    [property: Id(1)] CorrelationId Id,
    [property: Id(2)] SiloAddress TargetSilo,
    [property: Id(3)] int ForwardCount);

[Immutable]
internal struct GrainMigrationPackage
{
    // 即使完成操作与序列化发生竞争，传输中的有效载荷也只有一个所有者。
    [field: NonSerialized]
    internal ActivationMigration? Source { get; init; }

    [Id(0)]
    public GrainId GrainId { get; set; }

    [Id(1)]
    public MigrationContext MigrationContext { get; set; }

    /// <summary>
    /// Grain 迁移时已为其排队的请求（如果存在）。
    /// </summary>
    [Id(2)]
    public List<Message>? Requests { get; set; }

    internal void ReleasePayload()
    {
        MigrationContext?.Dispose();
        if (Requests is { } requests)
        {
            foreach (var request in requests)
            {
                request.ReleaseBodyBuffer();
            }
        }
    }
}

/// <summary>
/// 将激活实例迁移到新位置的功能。
/// </summary>
internal interface IActivationMigrationManager
{
    /// <summary>
    /// 开始将激活实例连同其排队请求迁移至指定目标。
    /// </summary>
    /// <param name="target">接收迁移的另一个 Silo。</param>
    /// <param name="source">迁移中的激活实例地址。</param>
    /// <param name="migrationContext">被迁移 Grain 的信息，将由新激活实例使用。</param>
    /// <param name="requests">已为激活实例排队的请求（如果存在）。迁移操作取得这些请求的所有权。</param>
    /// <param name="cancellationToken">
    /// 若尚未发送迁移，或目标已接管其请求且正在重定向调用方，此令牌可结束迁移。
    /// 一旦开始接受迁移，迁移操作便在系统响应超时的时限内等待其结果，包括传出调用筛选器的执行时间。
    /// </param>
    /// <returns>迁移操作。</returns>
    ActivationMigration StartMigration(SiloAddress target, GrainAddress source, MigrationContext migrationContext, List<Message>? requests, CancellationToken cancellationToken);
}

/// <summary>
/// 激活实例的迁移操作，它拥有已为该激活实例排队的请求。
/// </summary>
/// <remarks>
/// <para>
/// 激活实例开始迁移时移交其排队请求，并在迁移完成之前留在激活实例目录中，
/// 以便发往此 Silo 上该 Grain 的请求仍能到达它。离开激活实例目录后，它调用 <see cref="Deliver"/>
/// 处理初始移交批次，并通过普通停用流程处理剩余队列。
/// </para>
/// <para>
/// 序列化取得有效载荷的所有权，并在写入完成时释放它；完成操作释放未被认领的有效载荷。
/// 迁移失败或结果未知时拒绝请求，
/// 以便调用方重试。迁移未发送的请求会被转发，随后转发之后到达的请求。
/// </para>
/// </remarks>
internal sealed class ActivationMigration(
    ActivationMigrationManager manager,
    GrainAddress source,
    SiloAddress target,
    GrainMigrationPackage package,
    List<Message>? requests,
    List<(SiloAddress CallerSilo, MigratedRequestRoute Route)>? routes,
    long size,
    CancellationToken cancellationToken)
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenRegistration _cancellation;
    private Stage _stage;
    private bool _payloadOwnedBySerializer;

    private enum Stage { Queued, Sent, Accepted }

    /// <summary>
    /// 目标已响应，并且若目标接收了迁移中的请求，其调用方的重定向已经完成或激活实例的截止时间已过，此操作完成。
    /// 如果迁移失败，或在发送前已取消，则以故障结束。
    /// </summary>
    public Task Completion => _completion.Task;

    internal GrainMigrationPackage Package => package with { Source = this };

    /// <summary>
    /// 用于重定向迁移所携带请求的调用方的路由信息。
    /// </summary>
    internal List<(SiloAddress CallerSilo, MigratedRequestRoute Route)>? Routes { get; } = routes;

    /// <summary>
    /// 对数据包序列化后大小的估计。
    /// </summary>
    internal long Size { get; } = size;

    internal void RegisterCancellation()
    {
        lock (_completion)
        {
            _cancellation = cancellationToken.UnsafeRegister(static state => ((ActivationMigration)state!).OnCanceled(), this);
            if (Completion.IsCompleted)
            {
                // Register 可能在赋值注册对象之前同步调用回调。
                _cancellation.Unregister();
            }
        }
    }

    /// <summary>
    /// 除非迁移已取消或完成，否则确定迁移即将发送。
    /// </summary>
    internal bool TrySend()
    {
        lock (_completion)
        {
            if (Completion.IsCompleted)
            {
                return false;
            }

            Debug.Assert(_stage == Stage.Queued);
            _stage = Stage.Sent;
            return true;
        }
    }

    /// <summary>
    /// 将有效载荷移交给序列化器。此后完成操作可以独立于其缓冲区继续进行。
    /// </summary>
    internal void BeginSerialization()
    {
        lock (_completion)
        {
            Debug.Assert(_stage == Stage.Sent);
            if (Completion.IsCompleted)
            {
                throw new OperationCanceledException("The migration completed before its payload was serialized.");
            }

            if (_payloadOwnedBySerializer)
            {
                throw new InvalidOperationException("The migration payload has already been serialized. Transport retries use the serialized message.");
            }

            _payloadOwnedBySerializer = true;
        }
    }

    /// <summary>
    /// 记录接受结果，并完成没有待重定向项或已达到截止时间的迁移。
    /// </summary>
    internal void SetAccepted()
    {
        lock (_completion)
        {
            if (Completion.IsCompleted)
            {
                return;
            }

            Debug.Assert(_stage == Stage.Sent);
            _stage = Stage.Accepted;
            if (Routes is null || cancellationToken.IsCancellationRequested)
            {
                Complete();
            }
        }
    }

    /// <summary>
    /// 在接受、重定向或失败之后完成迁移。
    /// </summary>
    /// <param name="exception">导致迁移无法进行的错误（如果存在）。</param>
    internal void Complete(Exception? exception = null)
    {
        lock (_completion)
        {
            if (Completion.IsCompleted)
            {
                return;
            }

            if (!_payloadOwnedBySerializer)
            {
                if (_stage == Stage.Queued)
                {
                    Package.MigrationContext.Dispose();
                }
                else
                {
                    Package.ReleasePayload();
                }
            }

            // 注销注册，不等待可能正在等待此锁的取消回调。
            _cancellation.Unregister();
            if (exception is null)
            {
                _completion.SetResult();
            }
            else
            {
                _completion.SetException(exception);
            }
        }
    }

    /// <summary>
    /// 激活实例离开激活实例目录后，拒绝或转发迁移中的初始请求。
    /// </summary>
    /// <remarks>激活实例在 <see cref="Completion"/> 完成后仅调用此方法一次。</remarks>
    public void Deliver()
    {
        Debug.Assert(Completion.IsCompleted, "The migration delivers its requests once its outcome is known.");
        manager.DeliverRequests(source, target, carried: _stage != Stage.Queued && Package.Requests is not null, _stage == Stage.Accepted, requests);
        requests = null;
    }

    private void OnCanceled()
    {
        lock (_completion)
        {
            // 已发送的迁移等待其结果，然后决定如何处置请求。
            if (_stage != Stage.Sent)
            {
                Complete(_stage == Stage.Queued ? new OperationCanceledException(cancellationToken) : null);
            }
        }
    }
}

/// <summary>
/// 将 Grain 激活实例迁移至目标宿主，并处理其他宿主的迁移请求。
/// </summary>
internal sealed partial class ActivationMigrationManager : SystemTarget, IActivationMigrationManagerSystemTarget, IActivationMigrationManager, ILifecycleParticipant<ISiloLifecycle>
{
    /// <summary>
    /// <see cref="IActivationMigrationManagerSystemTarget"/> 接受迁移 Grain 时一同接受排队请求的接口版本。
    /// </summary>
    internal const ushort MigratedRequestsVersion = 1;

    private const int MaxBatchSize = 1_000;
    private const int MessageBodyFramingAllowance = 1_024;
    private const int PackageFramingAllowance = 64;
    private const int RequestFramingAllowance = 16;

    private readonly ConcurrentDictionary<SiloAddress, (Task PumpTask, Channel<ActivationMigration> WorkItemChannel)> _workers = new();
    private readonly CancellationTokenSource _shuttingDownCts = new();
    private readonly ILogger<ActivationMigrationManager> _logger;
    private readonly IInternalGrainFactory _grainFactory;
    private readonly Catalog _catalog;
    private readonly InsideRuntimeClient _runtimeClient;
    private readonly IClusterMembershipService _clusterMembershipService;
    private readonly IClusterManifestProvider _clusterManifestProvider;
    private readonly GrainInterfaceType _migrationInterfaceType;
    private MessageCenter? _messageCenter;
    private readonly int _maxMessageBodySize;
    private readonly TimeSpan _systemResponseTimeout;
    private readonly ActivationDirectory _activations;
    private readonly Lock _lock = new();

#pragma warning disable IDE0052 // Remove unread private members. 理由：此字段仅用于诊断。
    private readonly Task? _membershipUpdatesTask;
#pragma warning restore IDE0052 // Remove unread private members

    public ActivationMigrationManager(
        ILocalSiloDetails localSiloDetails,
        ILoggerFactory loggerFactory,
        IInternalGrainFactory grainFactory,
        Catalog catalog,
        SystemTargetShared shared,
        IClusterMembershipService clusterMembershipService,
        IOptions<SiloMessagingOptions> messagingOptions,
        IClusterManifestProvider clusterManifestProvider,
        GrainInterfaceTypeResolver interfaceTypeResolver) : base(Constants.ActivationMigratorType, shared)
    {
        _clusterManifestProvider = clusterManifestProvider;
        _migrationInterfaceType = interfaceTypeResolver.GetGrainInterfaceType(typeof(IActivationMigrationManagerSystemTarget));
        _maxMessageBodySize = messagingOptions.Value.MaxMessageBodySize;
        _systemResponseTimeout = messagingOptions.Value.SystemResponseTimeout;
        _activations = shared.ActivationDirectory;
        _grainFactory = grainFactory;
        _logger = loggerFactory.CreateLogger<ActivationMigrationManager>();
        _catalog = catalog;
        _runtimeClient = shared.RuntimeClient;
        _clusterMembershipService = clusterMembershipService;
        shared.ActivationDirectory.RecordNewTarget(this);

        {
            using var _ = new ExecutionContextSuppressor();
            _membershipUpdatesTask = Task.Factory.StartNew(
                state => ((ActivationMigrationManager)state!).ProcessMembershipUpdates(),
                this,
                CancellationToken.None,
                TaskCreationOptions.None,
                WorkItemGroup.TaskScheduler).Unwrap();
            _membershipUpdatesTask.Ignore();
        }
    }

    /// <summary>
    /// 获取首次使用时解析的消息中心，因为它可能依赖此迁移管理器。
    /// </summary>
    private MessageCenter MessageCenter => _messageCenter ??= _runtimeClient.ServiceProvider.GetRequiredService<MessageCenter>();

    public async ValueTask AcceptMigratingGrains(
        List<GrainMigrationPackage> migratingGrains,
        CancellationToken cancellationToken = default)
    {
        var nextPackage = 0;
        try
        {
            for (var i = 0; i < migratingGrains.Count; i++)
            {
                ValidateRequests(migratingGrains[i]);
            }

            var activations = new List<ActivationData>();
            var currentActivity = Activity.Current;
            while (nextPackage < migratingGrains.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var package = migratingGrains[nextPackage];
                var requests = TakeRequests(package, Silo);

                // 激活实例目录拥有上下文，并在失败时清理尚未接纳的请求。
                nextPackage++;
                var context = _catalog.GetOrCreateActivation(package.GrainId, requestContextData: null, package);
                if (context is null && requests is not null)
                {
                    var delivered = 0;
                    try
                    {
                        foreach (var request in requests)
                        {
                            delivered++;
                            MessageCenter.ReceiveMessage(request);
                        }
                    }
                    finally
                    {
                        for (var i = delivered; i < requests.Count; i++)
                        {
                            requests[i].Dispose();
                        }

                        requests.Clear();
                    }
                }

                if (context is ActivationData activation)
                {
                    activations.Add(activation);
                }

                Activity.Current = currentActivity;
            }

            // 等待所有激活实例进入活跃状态或到达终止状态。
            // 这确保返回之前激活实例已经完成目录注册（或已被放弃）。
            // 否则可能出现竞态：原 Silo 从其激活实例目录移除激活实例，收到发往该激活实例的新消息，
            // 并在此 Silo 上的新激活实例完成目录注册之前重新激活它。
            using var waitActivity = ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.WaitMigration);
            foreach (var activation in activations)
            {
                await activation.WaitForActivationReadyAsync(cancellationToken);
            }
        }
        finally
        {
            // 验证失败、取消或之前的数据包失败时，剩余部分仍归此调用所有。
            for (; nextPackage < migratingGrains.Count; nextPackage++)
            {
                var package = migratingGrains[nextPackage];
                package.MigrationContext.Dispose();
                if (package.Requests is { } requests)
                {
                    foreach (var request in requests)
                    {
                        request.Dispose();
                    }

                    requests.Clear();
                }
            }
        }
    }

    /// <summary>
    /// 确保数据包仅携带以其 Grain 为目标的请求。
    /// </summary>
    private static void ValidateRequests(GrainMigrationPackage package)
    {
        var grainId = package.GrainId;
        if (package.Requests?.Find(request => request.TargetGrain != grainId || request.IsLocalOnly || request.Direction is not (Message.Directions.Request or Message.Directions.OneWay)) is { } invalid)
        {
            throw new ArgumentException($"The migration package for grain {grainId} carries a message which is not a request for the grain: {invalid}", nameof(package));
        }
    }

    /// <summary>
    /// 取出数据包所携带的请求，此 Silo 现在拥有它们。
    /// </summary>
    /// <returns>请求集合；如果数据包未携带请求，则为 <see langword="null"/>。</returns>
    private static List<Message>? TakeRequests(GrainMigrationPackage package, SiloAddress silo)
    {
        if (package.Requests is not { Count: > 0 } requests)
        {
            return null;
        }

        // 与普通接收流程一致，逐个丢弃已过期请求。
        requests.RemoveAll(static request =>
        {
            if (!request.IsExpired)
            {
                return false;
            }

            request.Dispose();
            return true;
        });

        foreach (var request in requests)
        {
            // 迁移计作一次转发。
            request.TargetSilo = silo;
            request.ForwardCount++;
        }

        return requests;
    }

    public ValueTask RetargetMigratedRequests(List<MigratedRequestRoute> routes, CancellationToken cancellationToken = default)
    {
        foreach (var route in routes)
        {
            _runtimeClient.RetargetCallback(route.SendingGrain, route.Id, route.TargetSilo, route.ForwardCount);
        }

        return default;
    }

    public ActivationMigration StartMigration(SiloAddress targetSilo, GrainAddress source, MigrationContext migrationContext, List<Message>? requests, CancellationToken cancellationToken)
    {
        Debug.Assert(!targetSilo.Equals(source.SiloAddress));
        var grainId = source.GrainId;
        var package = new GrainMigrationPackage { GrainId = grainId, MigrationContext = migrationContext };

        // 限制上下文大小，无需再次序列化其中的值。
        var size = PackageFramingAllowance + migrationContext.GetMaxSerializedSize() + grainId.Type.AsSpan().Length + grainId.Key.AsSpan().Length;
        List<(SiloAddress CallerSilo, MigratedRequestRoute Route)>? routes = null;
        Exception? preparationError = null;
        if (requests is { Count: > 0 } && AcceptsMigratedRequests(targetSilo))
        {
            package.Requests = requests;
            routes = GetRoutes(targetSilo, requests);
            if (!cancellationToken.IsCancellationRequested)
            {
                var shared = _runtimeClient.ServiceProvider.GetRequiredService<MessageHandlerShared>();
                var serializer = shared.GetMessageSerializer();
                try
                {
                    using var buffer = new Orleans.Serialization.Buffers.ArcBufferWriter();
                    var requestsSize = 0L;
                    foreach (var request in requests)
                    {
                        requestsSize += serializer.PrepareRequestBody(shared, buffer, request)
                            + serializer.GetHeaderSizeBound(buffer, request) + Message.LENGTH_HEADER_SIZE + RequestFramingAllowance;
                        if (size + requestsSize > MaxBatchBodySize)
                        {
                            break;
                        }
                    }

                    if (size + requestsSize <= MaxBatchBodySize)
                    {
                        size += requestsSize;
                    }
                    else
                    {
                        package.Requests = null;
                        routes = null;
                        LogDebugMigratedRequestsTooLarge(requests.Count, grainId, targetSilo);
                    }
                }
                catch (Exception exception)
                {
                    preparationError = exception;
                }
                finally
                {
                    shared.Return(serializer);
                }
            }
        }

        var migration = new ActivationMigration(this, source, targetSilo, package, requests, routes, size, cancellationToken);
        if (preparationError is not null)
        {
            // 序列化已经消耗了这些值。使此迁移失败并拒绝其请求，而不是转发仅部分序列化的对象。
            migration.TrySend();
            migration.Complete(preparationError);
            return migration;
        }

        migration.RegisterCancellation();
        if (!GetOrCreateWorker(targetSilo).TryWrite(migration))
        {
            migration.Complete(new SiloUnavailableException($"Silo {targetSilo} is no longer active"));
        }

        return migration;
    }

    /// <summary>
    /// 获取 <paramref name="silo"/> 是否支持在迁移 Grain 时一同接受请求，因为运行较早版本的 Silo 不支持此功能。
    /// </summary>
    private bool AcceptsMigratedRequests(SiloAddress silo)
        => _clusterManifestProvider.Current.Silos.TryGetValue(silo, out var manifest)
            && manifest.Interfaces.TryGetValue(_migrationInterfaceType, out var properties)
            && properties.Properties.TryGetValue(WellKnownGrainInterfaceProperties.Version, out var value)
            && ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            && version >= MigratedRequestsVersion;

    /// <summary>
    /// 获取用于将 <paramref name="requests"/> 的调用方重定向到 <paramref name="target"/> 的路由信息。
    /// </summary>
    /// <remarks>
    /// 每个请求的发送 Silo 持有其回调，除非调用方是外部客户端，此时客户端通过其网关跟踪调用。
    /// 现在捕获路由：此 Silo 上的调用方与其回调共享同一请求消息，而同一请求的后续迁移可以在
    /// 本次迁移等待接受结果时重定向该回调。单向请求没有需要重定向的待完成回调。
    /// </remarks>
    private static List<(SiloAddress CallerSilo, MigratedRequestRoute Route)>? GetRoutes(SiloAddress target, List<Message> requests)
    {
        List<(SiloAddress CallerSilo, MigratedRequestRoute Route)>? routes = null;
        foreach (var request in requests)
        {
            if (request.Direction is not Message.Directions.OneWay && request.SendingSilo is { } callerSilo)
            {
                (routes ??= []).Add((callerSilo, new(request.SendingGrain, request.Id, target, request.ForwardCount + 1)));
            }
        }

        return routes;
    }

    /// <summary>
    /// 迁移结果已知且源激活实例已离开激活实例目录后，拒绝或转发迁移中的请求。
    /// </summary>
    /// <param name="source">迁移中的激活实例地址。</param>
    /// <param name="target">迁移目标。</param>
    /// <param name="carried">迁移是否已发送请求。</param>
    /// <param name="accepted">目标是否接受了迁移所发送的请求。</param>
    /// <param name="requests">迁移开始时已排队的请求。</param>
    internal void DeliverRequests(GrainAddress source, SiloAddress target, bool carried, bool accepted, List<Message>? requests)
    {
        if (requests is not { Count: > 0 })
        {
            return;
        }

        if (carried)
        {
            foreach (var request in requests)
            {
                if (!accepted && request.Direction is Message.Directions.Request)
                {
                    // 目标可能已取得请求，发送请求也可能消耗它，因此拒绝请求，让调用方可以重试。
                    // 拒绝操作仅读取路由标头，独立于正在工作的序列化器的有效载荷。
                    MessageCenter.SendRejectionResponse(request, Message.RejectionTypes.Transient, "The activation's migration did not deliver the request.", exception: null);
                }

                // 单向请求没有可以接收拒绝响应的调用方，因此将其丢弃，与单向请求投递失败时的处理一致。
            }

            return;
        }

        MessageCenter.ProcessRequestsToInvalidActivation(requests, source, target, "The activation migrated.");
    }

    private int MaxBatchBodySize => _maxMessageBodySize - MessageBodyFramingAllowance;

    private async Task ProcessMembershipUpdates()
    {
        await Task.Yield();

        try
        {
            LogDebugMonitoringUpdates();

            var previousSnapshot = _clusterMembershipService.CurrentSnapshot;
            await foreach (var snapshot in _clusterMembershipService.MembershipUpdates)
            {
                try
                {
                    var diff = snapshot.CreateUpdate(previousSnapshot);
                    previousSnapshot = snapshot;
                    foreach (var change in diff.Changes)
                    {
                        if (change.Status.IsTerminating())
                        {
                            RemoveWorker(change.SiloAddress);
                        }
                    }
                }
                catch (Exception exception)
                {
                    LogErrorProcessingMembershipUpdates(exception);
                }
            }
        }
        finally
        {
            LogDebugNoLongerMonitoring();
        }
    }

    private async Task PumpMigrationQueue(SiloAddress targetSilo, Channel<ActivationMigration> workItems)
    {
        try
        {
            var remote = _grainFactory.GetSystemTarget<IActivationMigrationManagerSystemTarget>(Constants.ActivationMigratorType, targetSilo);
            await Task.Yield();

            LogDebugStartingWorker(targetSilo);

            var maxBatchSize = MaxBatchBodySize;
            var items = new List<ActivationMigration>();
            var reader = workItems.Reader;
            while (await reader.WaitToReadAsync())
            {
                // 即使调用在传输层序列化批次之前超时，该调用也拥有其批次。
                var batch = new List<GrainMigrationPackage>();
                try
                {
                    // 收集消息大小限制以内的一批工作项。
                    long batchSize = 0;
                    while (batch.Count < MaxBatchSize
                        && reader.TryPeek(out var next)
                        && (batch.Count == 0 || batchSize + next.Size <= maxBatchSize)
                        && reader.TryRead(out var workItem))
                    {
                        if (!workItem.TrySend())
                        {
                            // 迁移在发送前已取消。
                            continue;
                        }

                        items.Add(workItem);
                        batch.Add(workItem.Package);
                        batchSize += workItem.Size;
                    }

                    if (items.Count == 0)
                    {
                        continue;
                    }

                    var acceptance = remote.AcceptMigratingGrains(batch, _shuttingDownCts.Token).AsTask();
                    // 筛选器可能延迟回调注册，或在此次等待超时后恢复执行。
                    acceptance.Ignore();
                    await acceptance.WaitAsync(_systemResponseTimeout, _runtimeClient.TimeProvider);
                    CompleteAfterRetargeting(items);

                    LogDebugMigratedActivations(items.Count, targetSilo);
                }
                catch (Exception exception)
                {
                    if (!_shuttingDownCts.IsCancellationRequested)
                    {
                        LogErrorMigratingActivations(exception, items.Count, targetSilo);
                    }

                    // 目标可能已取得批次携带的请求，因此拒绝而非重发它们。
                    foreach (var item in items)
                    {
                        item.Complete(exception);
                    }

                    // 如果 Silo 正在终止，则应停止尝试向其迁移激活实例。
                    if (_clusterMembershipService.CurrentSnapshot.GetSiloStatus(targetSilo).IsTerminating())
                    {
                        break;
                    }
                }
                finally
                {
                    items.Clear();
                }
            }

            // 移除自身并清理。
            RemoveWorker(targetSilo);
        }
        finally
        {
            LogDebugExitingWorker(targetSilo);
        }
    }

    /// <summary>
    /// 对目标已接受的请求的调用方完成重定向之后，完成工作项。
    /// </summary>
    /// <remarks>
    /// 重定向以尽力而为的方式进行。对于请求已被接受的工作项，调用方重定向完成、失败或到达截止时间时，该工作项即完成，
    /// 因此正在退出的源通常在其调用方跟随请求后离开。未被重定向的调用方会在源离开时调用失败。
    /// 其他工作项立即完成，不等待无关调用方。同时消息泵继续处理下一批。
    /// </remarks>
    private void CompleteAfterRetargeting(List<ActivationMigration> items)
    {
        Dictionary<SiloAddress, List<MigratedRequestRoute>>? routesBySilo = null;
        List<ActivationMigration>? awaitingRetarget = null;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            item.SetAccepted();
            if (item.Routes is not { } routes)
            {
                continue;
            }

            (awaitingRetarget ??= []).Add(item);
            foreach (var (callerSilo, route) in routes)
            {
                routesBySilo ??= [];
                if (!routesBySilo.TryGetValue(callerSilo, out var siloRoutes))
                {
                    routesBySilo[callerSilo] = siloRoutes = [];
                }

                siloRoutes.Add(route);
            }
        }

        if (awaitingRetarget is not null)
        {
            RetargetThenCompleteAsync(awaitingRetarget, routesBySilo!).Ignore();
        }

        async Task RetargetThenCompleteAsync(List<ActivationMigration> items, Dictionary<SiloAddress, List<MigratedRequestRoute>> routesBySilo)
        {
            try
            {
                var retargets = new List<Task>(routesBySilo.Count);
                foreach (var (callerSilo, routes) in routesBySilo)
                {
                    if (callerSilo.Equals(Silo))
                    {
                        retargets.Add(RetargetMigratedRequests(routes).AsTask());
                    }
                    else
                    {
                        var remote = _grainFactory.GetSystemTarget<IActivationMigrationManagerSystemTarget>(Constants.ActivationMigratorType, callerSilo);
                        retargets.Add(remote.RetargetMigratedRequests(routes, _shuttingDownCts.Token).AsTask());
                    }
                }

                await Task.WhenAll(retargets);
            }
            catch (Exception exception)
            {
                // 未被重定向的调用方（例如运行较早版本的调用方）会在源离开时调用失败。
                LogDebugRetargetMigratedRequestsFailed(exception, items.Count);
            }
            finally
            {
                foreach (var item in items)
                {
                    item.Complete();
                }
            }
        }
    }

    private ChannelWriter<ActivationMigration> GetOrCreateWorker(SiloAddress targetSilo)
    {
        if (!_workers.TryGetValue(targetSilo, out var entry))
        {
            lock (_lock)
            {
                if (!_workers.TryGetValue(targetSilo, out entry))
                {
                    using var _ = new ExecutionContextSuppressor();
                    var channel = Channel.CreateUnbounded<ActivationMigration>();
                    var pumpTask = Task.Factory.StartNew(
                        () => PumpMigrationQueue(targetSilo, channel),
                        CancellationToken.None,
                        TaskCreationOptions.None,
                        WorkItemGroup.TaskScheduler).Unwrap();
                    pumpTask.Ignore();

                    entry = (pumpTask, channel);
                    var didAdd = _workers.TryAdd(targetSilo, entry);
                    Debug.Assert(didAdd);
                }
            }
        }

        return entry.WorkItemChannel.Writer;
    }

    private void RemoveWorker(SiloAddress targetSilo)
    {
        if (_workers.TryRemove(targetSilo, out var entry))
        {
            LogDebugTargetSilo(targetSilo);

            entry.WorkItemChannel.Writer.TryComplete();

            var exception = new SiloUnavailableException($"Silo {targetSilo} is no longer active");
            while (entry.WorkItemChannel.Reader.TryRead(out var item))
            {
                item.Complete(exception);
            }
        }
    }

    private Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    private async Task StopAsync(CancellationToken cancellationToken)
    {
        var workerTasks = new List<Task>();
        foreach (var (_, value) in _workers)
        {
            value.WorkItemChannel.Writer.TryComplete();
            workerTasks.Add(value.PumpTask);
        }

        try
        {
            _shuttingDownCts.Cancel();
        }
        catch (Exception exception)
        {
            LogWarningSignalShutdownError(exception);
        }

        await Task.WhenAll(workerTasks).WaitAsync(cancellationToken).SuppressThrowing();
    }

    void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
    {
        lifecycle.Subscribe(
            nameof(ActivationMigrationManager),
            ServiceLifecycleStage.RuntimeGrainServices,
            QueuedStart,
            QueuedStop);

        Task QueuedStart(CancellationToken ct) => this.RunOrQueueTask(() => StartAsync(ct));
        Task QueuedStop(CancellationToken ct) => this.RunOrQueueTask(() => StopAsync(ct));
    }

    // 日志值类型。
    private readonly struct SiloAddressLogValue
    {
        private readonly SiloAddress _silo;
        public SiloAddressLogValue(SiloAddress silo) => _silo = silo;
        public override string ToString() => _silo.ToStringWithHashCode();
    }

    // 类末尾的日志记录方法。
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Monitoring cluster membership updates")]
    private partial void LogDebugMonitoringUpdates();

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error processing cluster membership updates"
    )]
    private partial void LogErrorProcessingMembershipUpdates(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "No longer monitoring cluster membership updates")]
    private partial void LogDebugNoLongerMonitoring();

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Starting migration worker for target silo {SiloAddress}")]
    private partial void LogDebugStartingWorker(SiloAddress siloAddress);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Migrated {Count} activations to target silo {SiloAddress}")]
    private partial void LogDebugMigratedActivations(int count, SiloAddress siloAddress);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error while migrating {Count} grain activations to {SiloAddress}")]
    private partial void LogErrorMigratingActivations(Exception exception, int count, SiloAddress siloAddress);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Exiting migration worker for target silo {SiloAddress}")]
    private partial void LogDebugExitingWorker(SiloAddress siloAddress);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Target silo {SiloAddress} is no longer active, so this migration activation worker is terminating"
    )]
    private partial void LogDebugTargetSilo(SiloAddress siloAddress);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Error signaling shutdown"
    )]
    private partial void LogWarningSignalShutdownError(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "The size bound for {Count} queued requests for migrating grain {GrainId} exceeds the migration message budget, so they are forwarded to {SiloAddress}."
    )]
    private partial void LogDebugMigratedRequestsTooLarge(int count, GrainId grainId, SiloAddress siloAddress);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Retargeting the callers of {Count} migrated grains failed. Callers which were not retargeted fail their calls when this silo departs."
    )]
    private partial void LogDebugRetargetMigratedRequestsFailed(Exception exception, int count);
}
