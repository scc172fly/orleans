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
/// 全部 grain 类的抽象基级。
/// </summary>
public abstract partial class Grain : IGrainBase, IAddressable
{
    // 因为我们目前没有提供注射的方法,
    // 任何与其的交互作用将产生非单位测试代码。
    // 客户代码(包括本类的子类别)中显示的“信息”应来自 IGrainRuntime。
    // 更好的解决办法是重新构思这个界面,使它可以通过构建器注射。
    /// <summary>
    /// 获取此 grain 激活的运行时间上下文。
    /// </summary>
    [AllowNull]
    public IGrainContext GrainContext { get; private set; }

    /// <summary>
    /// 获取此 grain的引用。
    /// </summary>
    public GrainReference GrainReference { get { return GrainContext.GrainReference; } }

    internal IGrainRuntime Runtime { get; }

    /// <summary>
    /// 获取一个可用于访问其它 grains的对象。如果此 grain 不与运行时间相关,例如直接为单位测试创建时,则该对象无效。
    /// </summary>
    protected IGrainFactory GrainFactory => Runtime.GrainFactory;

    /// <summary>
    /// 获取运行时管理 IServiceProvider 。如果此 grain 不与运行时间相关, 例如当直接为单位测试创建时, 则无效 。
    /// </summary>
    // 运行时间确保这不会无效,单位测试框架必须确保它不会无效。
    protected internal IServiceProvider ServiceProvider => GrainContext?.ActivationServices ?? Runtime?.ServiceProvider!;

    internal GrainId GrainId => GrainContext.GrainId;

    /// <summary>
    /// 绝对不应该引用这个构建器。 我们揭露它, 这样客户代码( Grain子类) 不必添加一个构建器 。
    /// 客户端代码应使用 GrainFactory 属性来获取 Grain的引用。
    /// </summary>
    // 运行时间确保这不会无效,单位测试框架必须确保它不会无效。
    protected Grain() : this(RuntimeContext.Current!, grainRuntime: null)
    { }

    /// <summary>
    /// Grain 实施者不必暴露该构建器,但可以选择这样做。
    /// 此构建器对单位测试特别有用, 测试代码可以创建 Grain 并替换
    /// 和有双倍试验的 IGrainIdentity 和 IGrainRuntime 。
    /// </summary>
    protected Grain(IGrainContext grainContext, IGrainRuntime? grainRuntime = null)
    {
        GrainContext = grainContext;

        // 运行时间确保这不会无效,单位测试框架必须确保它不会无效。
        Runtime = grainRuntime ?? grainContext?.ActivationServices.GetService<IGrainRuntime>()!;
    }

    /// <summary>
    /// 字符串表示 grain SiloIdentity ,包括类型和主键。
    /// </summary>
    public string IdentityString => GrainId.ToString();

    /// <summary>
    /// 当前 silo的独特标识符。
    /// 此字符串没有语义内容, 但可能对记录有用 。
    /// </summary>
    public string RuntimeIdentity => Runtime?.SiloIdentity ?? string.Empty;

    /// <summary>
    /// 注册一个向此 grain发送定期回调的计时器。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 此计时器不会阻止当前 grain 被禁用 。
    /// 如果该 grain 已停止使用,则定时器将被丢弃。
    /// </para>
    /// <para>
    /// 直到任务从回调中返回 解决了,
    /// 下次定时器时,将不会排期。
    /// 也就是说,时间推回从不中断转弯。
    /// </para>
    /// <para>
    /// 可随时调用 <c>Dispose</c> 方法,停止计时器
    /// 调用时, 计时器手柄上返回。
    /// </para>
    /// <para>
    /// 被调回时被任务推出或错出的任何例外 
    /// 将会登录, 但不会阻止下一次计时器的刻录 。
    /// </para>
    /// </remarks>
    /// <param name="callback">计时器勾选时要援引回调函数。</param>
    /// <param name="state">标的时作为辩论通过的国家 <paramref name="callback"/>.</param>
    /// <param name="dueTime">第一次定时器选中的合适时间。</param>
    /// <param name="period">后一个计时器计时周期。</param>
    /// <returns>处理此计时器 。</returns>
    [Obsolete("Use 'this.RegisterGrainTimer(callback, state, new() { DueTime = dueTime, Period = period, Interleave = true })' instead.")]
    protected IDisposable RegisterTimer(Func<object?, Task> callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        EnsureRuntime();

        // 运行时间确保这不会无效,单位测试框架必须确保它不会无效。
        return Runtime.TimerRegistry.RegisterTimer(GrainContext ?? RuntimeContext.Current!, callback, state, dueTime, period);
    }

    /// <summary>
    /// 解除此激活 grain 在当前 grain 方法调用已完成。
    /// 此调用将标志着当前 grain 的激活, 在当前方法结束时停止使用并移除 。
    /// 下次调用此 grain 将导致使用不同的激活, 典型地意味着在运行时将自动生成新的激活。
    /// </summary>
    protected void DeactivateOnIdle()
    {
        EnsureRuntime();

        // 运行时间确保这不会无效,单位测试框架必须确保它不会无效。
        Runtime.DeactivateOnIdle(GrainContext ?? RuntimeContext.Current!);
    }

    /// <summary>
    /// 开始尝试将这个实例移到另一个地方。
    /// 移民捕捉到当前的 <see cref="RequestContext"/>,向启动的安置主任提供,以便其在选择新地点时能够加以考虑。
    /// 移民将不同步地发生,而没有执行任何请求;如果启动的安置主任不选择其他地点,则不会出现移徙。
    /// </summary>
    protected void MigrateOnIdle()
    {
        EnsureRuntime();
        ((IGrainBase)this).MigrateOnIdle();
    }

    /// <summary>
    /// 延迟至少要在指定时间内取消激活 。
    /// <para>积极 <c>timeSpan</c> 值系指“防止GC在这段时间内启动该激活”。</para>
    /// <para>A <see cref="TimeSpan.Zero"/> 值中,“包括” <see cref="DelayDeactivation(TimeSpan)"/> 并按正常的启动垃圾收集设置,使这种激活行为成为行动”。</para>
    /// <para>A <see cref="Timeout.InfiniteTimeSpan"/> 值指“无限期停止使用”。</para>
    /// <para><see cref="DeactivateOnIdle"/> 将撤销/取代任何当前“保持生命”设置,
    /// 立即将 grain 用于禁用。</para>
    /// </summary>
    protected void DelayDeactivation(TimeSpan timeSpan)
    {
        EnsureRuntime();

        // 运行时间确保这不会无效,单位测试框架必须确保它不会无效。
        Runtime.DelayDeactivation(GrainContext ?? RuntimeContext.Current!, timeSpan);
    }

    /// <summary>
    /// 启动 grain的过程结束时,该方法被调用。
    /// 任何信息发至 grain之前,请先拨打该邮件。
    /// 对于宣布为持续状态的 grains ,这种方法在国有财产人口密集后使用。
    /// </summary>
    /// <param name="cancellationToken">取消符号, 当激活时的信号被取消时将发出此信号。</param>
    public virtual Task OnActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// 在解除 grain激活过程的开始时,就要求使用这种方法。
    /// </summary>
    /// <param name="reason">停止使用的理由。仅提供信息。</param>
    /// <param name="cancellationToken">取消符号,该符号在解除功能时发出信号后应立即完成。</param>
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
/// 宣布为持久性状态的 Grain 基级。
/// </summary>
/// <typeparam name="TGrainState">持久性状态物体的等级</typeparam>
public class Grain<TGrainState> : Grain
{
    /// <summary>
    /// 基本的国家储存。
    /// </summary>
    [AllowNull] // 这是由激活期间的运行时间设定的, 所以在构建器中可以无效 。
    private IStorage<TGrainState> _storage;

    /// <summary>
    /// 初始化 <see cref="Grain{TGrainState}"/> 类的新实例。
    /// </summary>
    /// <remarks>
    /// 绝对不应该引用这个构建器。 我们揭露它, 这样客户代码( 本类次分类) 就不必添加一个构建器了 。
    /// 客户端代码应使用 GrainFactory 来获取 Grain的引用。
    /// </remarks>
    protected Grain()
    {
        var observer = new LifecycleObserver(this);

        // 运行时间确保这不会无效,单位测试框架必须确保它不会无效。
        var lifecycle = RuntimeContext.Current!.ObservableLifecycle;
        lifecycle.AddMigrationParticipant(observer);
        lifecycle.Subscribe(RuntimeTypeNameFormatter.Format(GetType()), GrainLifecycleStage.SetupState, observer);
    }

    /// <summary>
    /// 初始化 <see cref="Grain{TGrainState}"/> 类的新实例。
    /// </summary>
    /// <param name="storage">
    /// 储存执行情况。
    /// </param>
    /// <remarks>
    /// Grain 实施者不必暴露该构建器,但可以选择这样做。
    /// 此构建器对单位测试特别有用, 测试代码可以创建 Grain 并替换
    /// (a) IGrainIdentity、 IGrainRuntime 和具有双倍试验的国家(模 / 根)。
    /// </remarks>
    protected Grain(IStorage<TGrainState> storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// 获取或设置 grain 状态。
    /// </summary>
    protected TGrainState State
    {
        get => _storage.State;
        set => _storage.State = value;
    }

    /// <summary>
    /// 从备份存储清除当前 grain 状态数据。
    /// </summary>
    /// <returns>
    /// 代表行动的一个 <see cref="Task"/> 。
    /// </returns>
    protected virtual Task ClearStateAsync() => _storage.ClearStateAsync();

    /// <summary>
    /// 将当前 grain 状态数据写入支持存储处。
    /// </summary>
    /// <returns>
    /// 代表行动的一个 <see cref="Task"/> 。
    /// </returns>
    protected virtual Task WriteStateAsync() => _storage.WriteStateAsync();

    /// <summary>
    /// 从备份存储处读取 grain 状态,更新 <see cref="State"/>。
    /// </summary>
    /// <remarks>
    /// grain 状态数据的任何先前内容将被覆盖。
    /// </remarks>
    /// <returns>
    /// 代表行动的一个 <see cref="Task"/> 。
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

            // 如果因体液补充而已经存在,则避免读取状态
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
