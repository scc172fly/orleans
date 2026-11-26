using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Storage;
using Orleans.TestingHost;

namespace Orleans.Persistence.TestKit;

/// <summary>
/// 用于测试的基底固定定型类 IGrainStorage 使用 InProcessTestCluster.
/// </summary>
/// <remarks>
/// 这一固定装置为测试储存供应商提供了一个在工艺 Orleans 中集。
/// 应用抽象方法配置存储提供者并检索以进行测试。
/// </remarks>
public abstract class GrainStorageTestFixture
{
    private ExceptionDispatchInfo? _preconditionException;
    private InProcessTestCluster? _cluster;
    private IGrainStorage? _storage;

    /// <summary>
    /// 获得在过程测试组群。
    /// </summary>
    protected InProcessTestCluster Cluster => _cluster ?? throw new InvalidOperationException("The test cluster has not been initialized.");

    /// <summary>
    /// 得到 grain 创建工厂 grain 参考文献。
    /// </summary>
    public IGrainFactory GrainFactory => Cluster.Client;

    /// <summary>
    /// 测试存储供应商
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
    /// 获取正在测试的存储供应商的名称 。
    /// </summary>
    protected abstract string StorageProviderName { get; }

    /// <summary>
    /// 在启动该组之前检查先决条件。
    /// 覆盖此选项以检查外部依赖性(e.g., Azure 存储模拟器)。
    /// </summary>
    /// <exception cref="Exception">如果不符合先决条件,就予以放弃。</exception>
    protected virtual void CheckPreconditionsOrThrow()
    {
    }

    /// <summary>
    /// 试验制造者称这为在先决条件失效时跳过试验。
    /// </summary>
    public void EnsurePreconditionsMet()
    {
        _preconditionException?.Throw();
    }

    /// <summary>
    /// 配置 silo ,与存储提供者一起测试。
    /// </summary>
    /// <param name="siloBuilder">要配置的 silo 构建器。</param>
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
    /// 配置其他的测试组选项 。
    /// </summary>
    /// <param name="builder">测试组群构建器。</param>
    protected virtual void ConfigureTestCluster(InProcessTestClusterBuilder builder)
    {
    }

    /// <summary>
    /// 初始化测试组群,解决配置的存储提供者。
    /// </summary>
    /// <returns>代表非同步初始化操作的任务 。</returns>
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
    /// 停止并处置测试组。
    /// </summary>
    /// <returns>一项代表非同步处置作业的任务。</returns>
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
