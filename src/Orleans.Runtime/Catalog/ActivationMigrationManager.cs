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
/// 将 grain 启动的远程界面迁移到 silo。
/// </summary>
/// <remarks>
/// 版本1接受已排队的移出 grains 的请求,并添加 <see cref="RetargetMigratedRequests"/>。
/// A silo 仅向该接口的列表版本 1 或稍后的 silos 发送排队请求。
/// </remarks>
[Version(ActivationMigrationManager.MigratedRequestsVersion)]
internal interface IActivationMigrationManagerSystemTarget : ISystemTarget
{
    /// <summary>
    /// 接受以最佳努力方式移民 grains ,以及为他们排队的请求。
    /// </summary>
    /// <param name="migratingGrains">移民 grains.</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <remarks>
    /// 该 silo 在接受电话时拥有请求的所有权,启动程序通过普通FIFO的接收程序接收。
    /// 新的启动程序在登记到 grain 目录之前收到请求。
    /// </remarks>
    [Alias("29E9E63F")]
    ValueTask AcceptMigratingGrains(
        [Immutable] List<GrainMigrationPackage> migratingGrains,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 将待决的 silo 调用电话移至接受其请求的 silos 。
    /// </summary>
    /// <param name="routes">重新瞄准的呼叫。</param>
    /// <param name="cancellationToken">取消牌。</param>
    [Alias("RetargetMigratedRequests")]
    ValueTask RetargetMigratedRequests(
        [Immutable] List<MigratedRequestRoute> routes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 确定了待发电话和现在提出请求的 silo 。
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
    // 即使在完成序列的竞赛中,电线有效载荷也只有一个所有人。
    [field: NonSerialized]
    internal ActivationMigration? Source { get; init; }

    [Id(0)]
    public GrainId GrainId { get; set; }

    [Id(1)]
    public MigrationContext MigrationContext { get; set; }

    /// <summary>
    /// 移民时排队的 grain 申请(如果有的话)。
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
/// 将启动器迁移到新地点的功能性。
/// </summary>
internal interface IActivationMigrationManager
{
    /// <summary>
    /// 开始将激活与排队申请迁移到指定目标。
    /// </summary>
    /// <param name="target">另一 silo 接受移徙。</param>
    /// <param name="source">迁移激活的地址。</param>
    /// <param name="migrationContext">关于正在迁移的 grain 的信息,新的激活将消耗这些信息。</param>
    /// <param name="requests">申请启动时排队的请求(如果有的话),迁移将获得所有权。</param>
    /// <param name="cancellationToken">
    /// 一种象征性的标志,它结束移民,如果尚未发送,或者目标已经响应其要求,而且他们的来电者正在成为目标。
    /// 一旦接受开始,迁移将等待结果,直至系统反应超时,包括发出电话过滤器。
    /// </param>
    /// <returns>移徙。</returns>
    ActivationMigration StartMigration(SiloAddress target, GrainAddress source, MigrationContext migrationContext, List<Message>? requests, CancellationToken cancellationToken);
}

/// <summary>
/// 启动程序迁移,该程序拥有被排队启动的请求。
/// </summary>
/// <remarks>
/// <para>
/// 启动程序在开始迁移时, 转移其排队的申请, 并留在目录中直到迁移完成
/// 提出此种请求的请求 silo 排排 grain 一旦它离开目录, 它就召唤 <see cref="Deliver"/>
/// 并处理其剩余队列,通过普通的停用。
/// </para>
/// <para>
/// 序列化取得有效载荷的所有权,并在写完后释放出来。完成后释放出一个未申报的有效载荷。
/// 如果移民失败或结果不明,则拒绝申请;
/// 移民没有发出申请,但随后又提出。
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
    /// 当目标得到回应,如果接受移民的要求,其呼叫者重新瞄准目标的工作已经完成或启动的最后期限已经过时,目标即告完成。
    /// 迁移失败或发送前取消时出现错误。
    /// </summary>
    public Task Completion => _completion.Task;

    internal GrainMigrationPackage Package => package with { Source = this };

    /// <summary>
    /// 重新确定移徙所提出要求的呼声的响应者目标路线。
    /// </summary>
    internal List<(SiloAddress CallerSilo, MigratedRequestRoute Route)>? Routes { get; } = routes;

    /// <summary>
    /// 估计包件的序列大小。
    /// </summary>
    internal long Size { get; } = size;

    internal void RegisterCancellation()
    {
        lock (_completion)
        {
            _cancellation = cancellationToken.UnsafeRegister(static state => ((ActivationMigration)state!).OnCanceled(), this);
            if (Completion.IsCompleted)
            {
                // 登记簿在指定登记之前可以同步援引回调。
                _cancellation.Unregister();
            }
        }
    }

    /// <summary>
    /// 将迁移到发送处,除非取消或完成。
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
    /// 将有效载荷转移到序列器上。 然后完成可以独立于缓冲进行。
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
    /// 记录接受和完成没有待决目标或已超过最后期限的迁移。
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
    /// 在接受、重新确定目标或失败后完成迁移。
    /// </summary>
    /// <param name="exception">阻止移民的错误,如果有的话。</param>
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

            // 无需等待取消回电即可解除登记, 可能正在等待这个锁 。
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
    /// 启动后,拒绝或转发移民的原始请求。
    /// </summary>
    /// <remarks>启动在 <see cref="Completion"/> 完成后,此时间的调用。</remarks>
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
            // 移民的遣送者在选择其申请处置方式之前等待结果。
            if (_stage != Stage.Sent)
            {
                Complete(_stage == Stage.Queued ? new OperationCanceledException(cancellationToken) : null);
            }
        }
    }
}

/// <summary>
/// 向目标主机启动的 Migrates grain 激活, 并处理来自其他主机的迁移请求。
/// </summary>
internal sealed partial class ActivationMigrationManager : SystemTarget, IActivationMigrationManagerSystemTarget, IActivationMigrationManager, ILifecycleParticipant<ISiloLifecycle>
{
    /// <summary>
    /// 接受已移出的排队申请的 <see cref="IActivationMigrationManagerSystemTarget"/> 版本 grains。
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

#pragma warning disable IDE0052 // Remove unread private members. Justification: this field is only for diagnostic purposes.
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
    /// 获取信息中心, 信息中心首先使用后就解决了, 因为信息中心可以依赖这个迁移管理器 。
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

                // 该目录拥有上下文,并清理未获准的失败请求。
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

            // 等待所有激活活动开始或到达终点状态。
            // 这确保了激活工作在我们返回之前完成在目录中的登记(或被放弃)。
            // 否则,可能会有一场竞赛,即原始 silo 从目录中删除激活,收到用于激活的新信息,
            // 并在此 silo 上的新激活登记在目录前重新激活它。
            using var waitActivity = ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.WaitMigration);
            foreach (var activation in activations)
            {
                await activation.WaitForActivationReadyAsync(cancellationToken);
            }
        }
        finally
        {
            // 验证、取消或失败的先前软件包留下属于该援引的剩余部分。
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
    /// 确保一揽子计划只载有针对其 grain的要求。
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
    /// 要求一个包件所包含、而 silo 现在拥有的要求。
    /// </summary>
    /// <returns>(a) 请求,或 <see langword="null"/> 如果包件没有运货,</returns>
    private static List<Message>? TakeRequests(GrainMigrationPackage package, SiloAddress silo)
    {
        if (package.Requests is not { Count: > 0 } requests)
        {
            return null;
        }

        // 与普通接待一样,个别地放弃过期申请。
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
            // 移徙算作向前推进。
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

        // 环绕上下文,不重复其价值第二次。
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
            // 序列化消耗了值。 如果无法进行这种迁移, 拒绝其请求, 而不是转发部分序列化的物品 。
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
    /// 获取是否 <paramref name="silo"/> 接受与迁移 grains一起的请求,因为运行较早版本的 silos 不接受。
    /// </summary>
    private bool AcceptsMigratedRequests(SiloAddress silo)
        => _clusterManifestProvider.Current.Silos.TryGetValue(silo, out var manifest)
            && manifest.Interfaces.TryGetValue(_migrationInterfaceType, out var properties)
            && properties.Properties.TryGetValue(WellKnownGrainInterfaceProperties.Version, out var value)
            && ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            && version >= MigratedRequestsVersion;

    /// <summary>
    /// 获取重新瞄准 <paramref name="requests"/> 呼叫器到 <paramref name="target"/>的线路。
    /// </summary>
    /// <remarks>
    /// 每项请求的发送 silo 都保留回调,除非打电话者是外部客户,对网关的通话进行跟踪。
    /// 现在选择路径:这个 silo 的呼叫者将请求信息与回呼相联,稍后又将同一请求迁移到另一个目标
    /// 单向请求没有等待回击以重新瞄准目标。
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
    /// 一旦知道移民的结果,其来源启动后就拒绝或转发移民请求,并离开目录。
    /// </summary>
    /// <param name="source">移民启动的地址</param>
    /// <param name="target">移徙目标。</param>
    /// <param name="carried">移民是否发出了申请。</param>
    /// <param name="accepted">目标是否接受了移民发出的请求。</param>
    /// <param name="requests">移民开始时排队处理的请求。</param>
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
                    // 目标可能已经接受了请求,发送者可能会消耗请求,因此被拒绝,其打电话者可以重新使用。
                    // 拒绝只读取路由信头, 独立于活性序列仪的有效载荷。
                    MessageCenter.SendRejectionResponse(request, Message.RejectionTypes.Transient, "The activation's migration did not deliver the request.", exception: null);
                }

                // 单向请求没有拒绝的电话,因此被放弃,因为单向请求在交付失败时是单向请求。
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
                // 即使运输在运输过程成序之前就已重复,该行业也拥有其批次。
                var batch = new List<GrainMigrationPackage>();
                try
                {
                    // 在信件大小限制内收集批次的工作项目 。
                    long batchSize = 0;
                    while (batch.Count < MaxBatchSize
                        && reader.TryPeek(out var next)
                        && (batch.Count == 0 || batchSize + next.Size <= maxBatchSize)
                        && reader.TryRead(out var workItem))
                    {
                        if (!workItem.TrySend())
                        {
                            // 移民在被送出之前被取消了
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
                    // 过滤器可以延迟回溯登记, 或者在等待过后恢复 。
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

                    // 目标可能已经接受了批次所提出的请求,因此被拒绝而不是怨恨。
                    foreach (var item in items)
                    {
                        item.Complete(exception);
                    }

                    // 如果 silo 终止,我们就应该停止试图将活性电动转移到它。
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

            // 自己去收拾干净
            RemoveWorker(targetSilo);
        }
        finally
        {
            LogDebugExitingWorker(targetSilo);
        }
    }

    /// <summary>
    /// 在重新瞄准目标接受的请求的打电话者之后,完成工作项目。
    /// </summary>
    /// <remarks>
    /// 重新确定目标是最努力的。 一旦其调用者重新确定目标完成、失败或最后期限过后,其申请被接受的工作项目即告完成,
    /// 如此,退休的源通常在调用者按其要求后离开。未调整目标的调用者在调用者离开时不接用。
    /// 其他工程项目立即完成,不等待无关的呼叫者。 泵将同时与下一批连接。
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
                // 一位没有目标的呼叫者,如运行较早版本的呼叫者,在源头离开时,未按其指令拨打。
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

    // 日志值类型
    private readonly struct SiloAddressLogValue
    {
        private readonly SiloAddress _silo;
        public SiloAddressLogValue(SiloAddress silo) => _silo = silo;
        public override string ToString() => _silo.ToStringWithHashCode();
    }

    // 类末的错格方法
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
