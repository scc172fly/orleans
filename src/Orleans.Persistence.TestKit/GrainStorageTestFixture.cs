using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Storage;
using Orleans.TestingHost;

namespace Orleans.Persistence.TestKit;

/// <summary>
/// 使用 InProcessTestCluster 测试 IGrainStorage 实现的基础测试夹具类。
/// </summary>
/// <remarks>
/// 此测试夹具提供进程内 Orleans 集群，用于测试存储提供程序。
/// 实现抽象方法，以配置存储提供程序并获取它以供测试。
/// </remarks>
public abstract class GrainStorageTestFixture
{
    private ExceptionDispatchInfo? _preconditionException;
    private InProcessTestCluster? _cluster;
    private IGrainStorage? _storage;

    /// <summary>
    /// 获取进程内测试集群。
    /// </summary>
    protected InProcessTestCluster Cluster => _cluster ?? throw new InvalidOperationException("The test cluster has not been initialized.");

    /// <summary>
    /// 获取用于创建 Grain 引用的 Grain 工厂。
    /// </summary>
    public IGrainFactory GrainFactory => Cluster.Client;

    /// <summary>
    /// 获取正在测试的存储提供程序。
    /// </summary>
    public IGrainStorage Storage
    {
        get
        {
            _preconditionException?.Throw();
            return _storage ?? throw new InvalidOperationException("The storage provider has not been initialized.");
        }
    }

    /// <summary>
    /// 获取正在测试的存储提供程序的名称。
    /// </summary>
    protected abstract string StorageProviderName { get; }

    /// <summary>
    /// 在初始化集群之前检查前置条件。
    /// 重写此方法以检查外部依赖项（例如 Azure Storage 模拟器）。
    /// </summary>
    /// <exception cref="Exception">前置条件未满足时抛出。</exception>
    protected virtual void CheckPreconditionsOrThrow()
    {
    }

    /// <summary>
    /// 确保满足前置条件。在测试构造函数中调用此方法，可在前置条件不满足时跳过测试。
    /// </summary>
    public void EnsurePreconditionsMet()
    {
        _preconditionException?.Throw();
    }

    /// <summary>
    /// 为 Silo 配置待测试的存储提供程序。
    /// </summary>
    /// <param name="siloBuilder">要配置的 Silo 构建器。</param>
    /// <example>
    /// <code>
    /// protected override void ConfigureSilo(ISiloBuilder siloBuilder)
    /// {
    ///     siloBuilder.AddMemoryGrainStorage("TestStorage");
    /// }
    /// </code>
    /// </example>
    protected abstract void ConfigureSilo(ISiloBuilder siloBuilder);

    /// <summary>
    /// 根据需要配置其他测试集群选项。
    /// </summary>
    /// <param name="builder">测试集群构建器。</param>
    protected virtual void ConfigureTestCluster(InProcessTestClusterBuilder builder)
    {
    }

    /// <summary>
    /// 初始化测试集群，并解析已配置的存储提供程序。
    /// </summary>
    /// <returns>表示异步初始化操作的任务。</returns>
    public virtual async ValueTask InitializeAsync()
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
        await cluster.DeployAsync().ConfigureAwait(false);
        _cluster = cluster;

        _storage = cluster.Silos[0].ServiceProvider.GetRequiredKeyedService<IGrainStorage>(StorageProviderName);
    }

    /// <summary>
    /// 停止并释放测试集群。
    /// </summary>
    /// <returns>表示异步释放操作的任务。</returns>
    public virtual async ValueTask DisposeAsync()
    {
        if (_cluster is not { } cluster)
        {
            return;
        }

        try
        {
            await cluster.StopAllSilosAsync().ConfigureAwait(false);
        }
        finally
        {
            await cluster.DisposeAsync().ConfigureAwait(false);
        }
    }
}
