using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Core;
using Orleans.Runtime;
using Orleans.Serialization.TypeSystem;

namespace Orleans;

/// <summary>
/// 所有 Grain 类的抽象基类。
/// </summary>
public abstract partial class Grain : IGrainBase, IAddressable
{
    // 不要直接使用它，因为目前没有提供注入它的方式；
    // 与它交互会导致代码无法进行单元测试。客户端代码
    // （包括此类的子类）能够访问的任何行为都应通过 IGrainRuntime 公开。
    // 更好的解决方案是重构此接口，使其可以通过构造函数注入。
    /// <summary>
    /// 获取此 Grain 激活实例的运行时上下文。
    /// </summary>
    [AllowNull]
    public IGrainContext GrainContext { get; private set; }

    /// <summary>
    /// 获取对此 Grain 的引用。
    /// </summary>
    public GrainReference GrainReference { get { return GrainContext.GrainReference; } }

    internal IGrainRuntime Runtime { get; }

    /// <summary>
    /// 获取一个可用于访问其他 Grain 的对象。如果此 Grain 未关联运行时（例如直接为单元测试创建时），则为 null。
    /// </summary>
    protected IGrainFactory GrainFactory => Runtime.GrainFactory;

    /// <summary>
    /// 获取由运行时管理的 IServiceProvider。如果此 Grain 未关联运行时（例如直接为单元测试创建时），则为 null。
    /// </summary>
    // ! 运行时保证此值不为 null，单元测试框架也必须保证此值不为 null。
    protected internal IServiceProvider ServiceProvider => GrainContext?.ActivationServices ?? Runtime?.ServiceProvider!;

    internal GrainId GrainId => GrainContext.GrainId;

    /// <summary>
    /// 不应主动调用此构造函数。公开它是为了让客户端代码（Grain 的子类）不必添加构造函数。
    /// 客户端代码应使用 GrainFactory 属性获取 Grain 引用。
    /// </summary>
    // ! 运行时保证此值不为 null，单元测试框架也必须保证此值不为 null。
    protected Grain() : this(RuntimeContext.Current!, grainRuntime: null)
    { }

    /// <summary>
    /// Grain 的实现者不必公开此构造函数，但可以选择公开它。
    /// 此构造函数尤其适用于单元测试，测试代码可以创建 Grain，并将
    /// IGrainIdentity 和 IGrainRuntime 替换为测试替身（mock/stub）。
    /// </summary>
    protected Grain(IGrainContext grainContext, IGrainRuntime? grainRuntime = null)
    {
        GrainContext = grainContext;

        // ! 运行时保证此值不为 null，单元测试框架也必须保证此值不为 null。
        Runtime = grainRuntime ?? grainContext?.ActivationServices.GetService<IGrainRuntime>()!;
    }

    /// <summary>
    /// Grain 的 SiloIdentity 的字符串表示形式，包括类型和主键。
    /// </summary>
    public string IdentityString => GrainId.ToString();

    /// <summary>
    /// 当前 Silo 的唯一标识符。
    /// 此字符串本身不具有语义含义，但可用于日志记录。
    /// </summary>
    public string RuntimeIdentity => Runtime?.SiloIdentity ?? string.Empty;

    /// <summary>
    /// 注册一个定时器，以便定期向此 Grain 发起回调。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 此定时器不会阻止当前 Grain 被停用。
    /// 如果 Grain 被停用，定时器也将被丢弃。
    /// </para>
    /// <para>
    /// 在回调返回的 Task 完成之前，
    /// 不会调度下一次定时器触发。
    /// 也就是说，同一定时器的回调执行轮次绝不会相互交错。
    /// </para>
    /// <para>
    /// 可以随时通过调用 <c>Dispose</c> 方法停止定时器，
    /// 该方法位于本次调用返回的定时器句柄上。
    /// </para>
    /// <para>
    /// 回调抛出的任何异常或回调返回的 Task 中的故障异常
    /// 都会被记录，但不会阻止下一次定时器触发进入队列。
    /// </para>
    /// </remarks>
    /// <param name="callback">定时器触发时调用的回调函数。</param>
    /// <param name="state">调用 <paramref name="callback"/> 时作为参数传入的状态对象。</param>
    /// <param name="dueTime">首次定时器触发前的延迟时间。</param>
    /// <param name="period">后续定时器触发的周期。</param>
    /// <returns>此定时器的句柄。</returns>
    [Obsolete("Use 'this.RegisterGrainTimer(callback, state, new() { DueTime = dueTime, Period = period, Interleave = true })' instead.")]
    protected IDisposable RegisterTimer(Func<object?, Task> callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        EnsureRuntime();

        // ! 运行时保证此值不为 null，单元测试框架也必须保证此值不为 null。
        return Runtime.TimerRegistry.RegisterTimer(GrainContext ?? RuntimeContext.Current!, callback, state, dueTime, period);
    }

    /// <summary>
    /// 当前 Grain 方法调用完成后，停用此 Grain 激活实例。
    /// 此调用将标记当前 Grain 的此激活实例，使其在当前方法结束后被停用并移除。
    /// 下一次调用此 Grain 时将使用另一个激活实例，通常意味着由运行时自动创建新的激活实例。
    /// </summary>
    protected void DeactivateOnIdle()
    {
        EnsureRuntime();

        // ! 运行时保证此值不为 null，单元测试框架也必须保证此值不为 null。
        Runtime.DeactivateOnIdle(GrainContext ?? RuntimeContext.Current!);
    }

    /// <summary>
    /// 开始尝试将此实例迁移到另一个位置。
    /// 迁移会捕获当前 <see cref="RequestContext"/>，并将其提供给此激活实例的放置决策器，以便选择新位置时加以考虑。
    /// 迁移将在没有正在执行的请求时异步进行；如果此激活实例的放置决策器未选出其他位置，则不会迁移。
    /// </summary>
    protected void MigrateOnIdle()
    {
        EnsureRuntime();
        ((IGrainBase)this).MigrateOnIdle();
    }

    /// <summary>
    /// 将此激活实例的停用至少延迟指定的时长。
    /// <para>正数 <c>timeSpan</c> 表示“在该时长内阻止回收此激活实例”。</para>
    /// <para><see cref="TimeSpan.Zero"/> 表示“取消上一次 <see cref="DelayDeactivation(TimeSpan)"/> 调用所设置的延迟，恢复此激活实例的常规激活实例垃圾回收设置”。</para>
    /// <para><see cref="Timeout.InfiniteTimeSpan"/> 表示“无限期延迟停用”。</para>
    /// <para><see cref="DeactivateOnIdle"/> 会撤销或覆盖当前所有“保持活动”设置，
    /// 使此 Grain 立即具备被停用的条件。</para>
    /// </summary>
    protected void DelayDeactivation(TimeSpan timeSpan)
    {
        EnsureRuntime();

        // ! 运行时保证此值不为 null，单元测试框架也必须保证此值不为 null。
        Runtime.DelayDeactivation(GrainContext ?? RuntimeContext.Current!, timeSpan);
    }

    /// <summary>
    /// 此方法在 Grain 激活过程的最后阶段调用。
    /// 在向 Grain 分派任何消息之前调用此方法。
    /// 对于声明了持久化状态的 Grain，在 State 属性填充完成后调用此方法。
    /// </summary>
    /// <param name="cancellationToken">指示正在取消激活操作的取消令牌。</param>
    public virtual Task OnActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// 此方法在 Grain 停用过程开始时调用。
    /// </summary>
    /// <param name="reason">停用原因，仅供参考。</param>
    /// <param name="cancellationToken">指示停用过程应尽快完成的取消令牌。</param>
    public virtual Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken) => Task.CompletedTask;

    internal void EnsureRuntime()
    {
        if (Runtime == null)
        {
            throw new InvalidOperationException("Grain was created outside of the Orleans creation process and no runtime was specified.");
        }
    }
}

/// <summary>
/// 声明了持久化状态的 Grain 的基类。
/// </summary>
/// <typeparam name="TGrainState">持久化状态对象的类。</typeparam>
public class Grain<TGrainState> : Grain
{
    /// <summary>
    /// 底层状态存储。
    /// </summary>
    [AllowNull] // 此字段由运行时在激活期间设置，因此在构造函数中可能为 null。
    private IStorage<TGrainState> _storage;

    /// <summary>
    /// 初始化 <see cref="Grain{TGrainState}"/> 类的新实例。
    /// </summary>
    /// <remarks>
    /// 不应主动调用此构造函数。公开它是为了让客户端代码（此类的子类）不必添加构造函数。
    /// 客户端代码应使用 GrainFactory 获取 Grain 引用。
    /// </remarks>
    protected Grain()
    {
        var observer = new LifecycleObserver(this);

        // ! 运行时保证此值不为 null，单元测试框架也必须保证此值不为 null。
        var lifecycle = RuntimeContext.Current!.ObservableLifecycle;
        lifecycle.AddMigrationParticipant(observer);
        lifecycle.Subscribe(RuntimeTypeNameFormatter.Format(GetType()), GrainLifecycleStage.SetupState, observer);
    }

    /// <summary>
    /// 初始化 <see cref="Grain{TGrainState}"/> 类的新实例。
    /// </summary>
    /// <param name="storage">
    /// 存储实现。
    /// </param>
    /// <remarks>
    /// Grain 的实现者不必公开此构造函数，但可以选择公开它。
    /// 此构造函数尤其适用于单元测试，测试代码可以创建 Grain，并将
    /// IGrainIdentity、IGrainRuntime 和 State 替换为测试替身（mock/stub）。
    /// </remarks>
    protected Grain(IStorage<TGrainState> storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// 获取或设置 Grain 状态。
    /// </summary>
    protected TGrainState State
    {
        get => _storage.State;
        set => _storage.State = value;
    }

    /// <summary>
    /// 从后端存储清除当前 Grain 状态数据。
    /// </summary>
    /// <returns>
    /// 表示该操作的 <see cref="Task"/>。
    /// </returns>
    protected virtual Task ClearStateAsync() => _storage.ClearStateAsync();

    /// <summary>
    /// 将当前 Grain 状态数据写入后端存储。
    /// </summary>
    /// <returns>
    /// 表示该操作的 <see cref="Task"/>。
    /// </returns>
    protected virtual Task WriteStateAsync() => _storage.WriteStateAsync();

    /// <summary>
    /// 从后端存储读取 Grain 状态并更新 <see cref="State"/>。
    /// </summary>
    /// <remarks>
    /// Grain 状态数据原有的所有内容都将被覆盖。
    /// </remarks>
    /// <returns>
    /// 表示该操作的 <see cref="Task"/>。
    /// </returns>
    protected virtual Task ReadStateAsync() => _storage.ReadStateAsync();

    private class LifecycleObserver : ILifecycleObserver, IGrainMigrationParticipant
    {
        private const string StorageMigratedKey = "grain-state-migrated";
        private readonly Grain<TGrainState> _grain;
        private bool _isInitialized;

        public LifecycleObserver(Grain<TGrainState> grain) => _grain = grain;

        private void SetupStorage() => _grain._storage ??= _grain.Runtime.GetStorage<TGrainState>(_grain.GrainContext);

        public void OnDehydrate(IDehydrationContext dehydrationContext)
        {
            var storage = _grain._storage;
            if (storage is IGrainMigrationParticipant migrationParticipant)
            {
                dehydrationContext.TryAddValue(StorageMigratedKey, true);
                migrationParticipant.OnDehydrate(dehydrationContext);
            }
        }

        public void OnRehydrate(IRehydrationContext rehydrationContext)
        {
            SetupStorage();
            if (_grain._storage is IGrainMigrationParticipant migrationParticipant)
            {
                _isInitialized = rehydrationContext.TryGetValue(StorageMigratedKey, out bool isMigrated) && isMigrated;
                migrationParticipant.OnRehydrate(rehydrationContext);
            }
        }

        public Task OnStart(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            // 如果已经通过迁移恢复获得状态，则无需再次读取状态。
            if (_isInitialized || _grain._storage?.Etag is not null)
            {
                return Task.CompletedTask;
            }

            SetupStorage();
            return _grain.ReadStateAsync();
        }

        public Task OnStop(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
