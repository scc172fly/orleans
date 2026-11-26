using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace Orleans.Reminders.TestKit;

/// <summary>
/// 使用 <see cref="IReminderTable"/> 由《公约》执行《公约》的 silo.
/// </summary>
/// <remarks>
/// 固定置件部署 <see cref="InProcessTestCluster"/>,配置正在测试的供应商
/// <see cref="ConfigureSilo"/>,并从第一个 silo中解析提醒表。
/// <see cref="InitializeAsync()"/> 和 <see cref="DisposeAsync()"/> 与生命周期钩 你测试框架。
/// </remarks>
public abstract class ReminderTableTestFixture
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromMinutes(1);
    private ExceptionDispatchInfo? _preconditionException;
    private InProcessTestCluster? _cluster;
    private IReminderTable? _reminderTable;

    /// <summary>
    /// 获得在过程测试组群中部署的测试组群。
    /// </summary>
    protected InProcessTestCluster Cluster => _cluster ?? throw new InvalidOperationException("The test cluster has not been initialized.");

    /// <summary>
    /// 获取集束客户端的 grain 工厂。
    /// </summary>
    public IGrainFactory GrainFactory => Cluster.Client;

    /// <summary>
    /// 测试提醒表
    /// </summary>
    public IReminderTable ReminderTable
    {
        get
        {
            _preconditionException?.Throw();
            return _reminderTable ?? throw new InvalidOperationException("The reminder table has not been initialized.");
        }
    }

    /// <summary>
    /// 配置 silo ,并使用提醒提供者测试。
    /// </summary>
    /// <param name="siloBuilder">下 silo 建筑商</param>
    protected abstract void ConfigureSilo(ISiloBuilder siloBuilder);

    /// <summary>
    /// 配置额外的测试组选项 。
    /// </summary>
    /// <param name="builder">测试组群构建器。</param>
    protected virtual void ConfigureTestCluster(InProcessTestClusterBuilder builder)
    {
    }

    /// <summary>
    /// 解决 silo服务供应商在测试中的提醒表。
    /// </summary>
    /// <param name="services">下 silo服务供应商。</param>
    /// <returns>提醒表。</returns>
    protected virtual IReminderTable ResolveReminderTable(IServiceProvider services) => services.GetRequiredService<IReminderTable>();

    /// <summary>
    /// 检查是否有外部服务等先决条件。
    /// </summary>
    protected virtual void CheckPreconditionsOrThrow()
    {
    }

    /// <summary>
    /// 重置所捕捉的前提条件失败,如果有的话。
    /// </summary>
    public void EnsurePreconditionsMet() => _preconditionException?.Throw();

    /// <summary>
    /// 部署该组群并解决提醒表。
    /// </summary>
    /// <returns>代表非同步初始化的任务。</returns>
    public virtual ValueTask InitializeAsync() => InitializeAsync(CancellationToken.None);

    /// <summary>
    /// 部署该组群并解决提醒表。
    /// </summary>
    /// <param name="cancellationToken">取消的符号。</param>
    /// <returns>代表非同步初始化的任务。</returns>
    public virtual async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            CheckPreconditionsOrThrow();
        }
        catch (Exception exception)
        {
            _preconditionException = ExceptionDispatchInfo.Capture(exception);
            return;
        }

        var builder = new InProcessTestClusterBuilder();
        builder.ConfigureSilo((_, siloBuilder) => ConfigureSilo(siloBuilder));
        ConfigureTestCluster(builder);

        var cluster = builder.Build();
        try
        {
            await cluster.DeployAsync(cancellationToken).ConfigureAwait(false);
            var reminderTable = ResolveReminderTable(cluster.Silos[0].ServiceProvider);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellation.CancelAfter(TimeSpan.FromMinutes(1));
            await reminderTable.StartAsync(cancellation.Token).ConfigureAwait(false);
            _cluster = cluster;
            _reminderTable = reminderTable;
        }
        catch
        {
            using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
            await cluster.DisposeAsync().AsTask().WaitAsync(cleanupCancellation.Token).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 停止并处置该组。
    /// </summary>
    /// <returns>代表非同步处置的任务。</returns>
    public virtual ValueTask DisposeAsync() => DisposeAsync(CancellationToken.None);

    /// <summary>
    /// 停止并处置该组。
    /// </summary>
    /// <param name="cancellationToken">取消的符号。</param>
    /// <returns>代表非同步处置的任务。</returns>
    public virtual async ValueTask DisposeAsync(CancellationToken cancellationToken)
    {
        if (_cluster is not { } cluster)
        {
            return;
        }

        try
        {
            await cluster.StopAllSilosAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
            await cluster.DisposeAsync().AsTask().WaitAsync(cleanupCancellation.Token).ConfigureAwait(false);
        }
    }
}
