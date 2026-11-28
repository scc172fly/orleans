using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace Orleans.Reminders.TestKit;

/// <summary>
/// 用于验证 Silo 托管的 <see cref="IReminderTable"/> 实现的基础测试夹具。
/// </summary>
/// <remarks>
/// 测试夹具部署一个 <see cref="InProcessTestCluster"/>，通过
/// <see cref="ConfigureSilo"/> 配置待测试提供程序，并从第一个 Silo 解析提醒器表。将
/// <see cref="InitializeAsync()"/> 和 <see cref="DisposeAsync()"/> 连接到测试框架的生命周期钩子。
/// </remarks>
public abstract class ReminderTableTestFixture
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromMinutes(1);
    private ExceptionDispatchInfo? _preconditionException;
    private InProcessTestCluster? _cluster;
    private IReminderTable? _reminderTable;

    /// <summary>
    /// 获取已部署的进程内测试集群。
    /// </summary>
    protected InProcessTestCluster Cluster => _cluster ?? throw new InvalidOperationException("The test cluster has not been initialized.");

    /// <summary>
    /// 获取集群客户端的 Grain 工厂。
    /// </summary>
    public IGrainFactory GrainFactory => Cluster.Client;

    /// <summary>
    /// 获取待测试的提醒器表。
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
    /// 为 Silo 配置待测试的提醒器提供程序。
    /// </summary>
    /// <param name="siloBuilder">Silo 构建器。</param>
    protected abstract void ConfigureSilo(ISiloBuilder siloBuilder);

    /// <summary>
    /// 配置其他测试集群选项。
    /// </summary>
    /// <param name="builder">测试集群构建器。</param>
    protected virtual void ConfigureTestCluster(InProcessTestClusterBuilder builder)
    {
    }

    /// <summary>
    /// 从 Silo 的服务提供程序解析待测试的提醒器表。
    /// </summary>
    /// <param name="services">Silo 的服务提供程序。</param>
    /// <returns>提醒器表。</returns>
    protected virtual IReminderTable ResolveReminderTable(IServiceProvider services) => services.GetRequiredService<IReminderTable>();

    /// <summary>
    /// 检查前置条件，例如外部服务的可用性。
    /// </summary>
    protected virtual void CheckPreconditionsOrThrow()
    {
    }

    /// <summary>
    /// 重新抛出捕获的前置条件失败（如果有）。
    /// </summary>
    public void EnsurePreconditionsMet() => _preconditionException?.Throw();

    /// <summary>
    /// 部署集群并解析提醒器表。
    /// </summary>
    /// <returns>表示异步初始化的任务。</returns>
    public virtual ValueTask InitializeAsync() => InitializeAsync(CancellationToken.None);

    /// <summary>
    /// 部署集群并解析提醒器表。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步初始化的任务。</returns>
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
    /// 停止并释放集群。
    /// </summary>
    /// <returns>表示异步释放的任务。</returns>
    public virtual ValueTask DisposeAsync() => DisposeAsync(CancellationToken.None);

    /// <summary>
    /// 停止并释放集群。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步释放的任务。</returns>
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
