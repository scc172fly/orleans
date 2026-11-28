using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Persistence;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.Serializers;
using Orleans.Storage;
using StackExchange.Redis;
using Tester;
using TestExtensions;
using Xunit;

public class CommonFixture : TestEnvironmentFixture
{
    /// <summary>
    /// 缓存 DefaultProviderRuntime 以供重复使用。
    /// </summary>
    private IProviderRuntime DefaultProviderRuntime { get; }

    /// <summary>
    /// 构造函数。
    /// </summary>
    public CommonFixture()
    {
        _ = this.Services.GetRequiredService<IOptions<ClusterOptions>>();
        DefaultProviderRuntime = new ClientProviderRuntime(
            this.InternalGrainFactory,
            this.Services,
            this.Services.GetRequiredService<ClientGrainContext>());
    }

    /// <summary>
    /// 根据环境变量返回适当的持久化提供程序实现。
    /// </summary>
    /// <remarks>如果创建存储提供程序时未满足环境不变条件，
    /// 则返回 <em>null</em> 值。</remarks>
    public async Task<IGrainStorage> CreateRedisGrainStorage(
        bool useOrleansSerializer = false,
        bool deleteStateOnClear = false,
        CancellationToken cancellationToken = default)
    {
        TestUtils.CheckForRedis();
        IGrainStorageSerializer grainStorageSerializer = useOrleansSerializer ? new OrleansGrainStorageSerializer(this.DefaultProviderRuntime.ServiceProvider.GetService<Serializer>()!)
                                                                              : new JsonGrainStorageSerializer(this.DefaultProviderRuntime.ServiceProvider.GetService<OrleansJsonSerializer>()!);
        var options = new RedisStorageOptions
        {
            ConfigurationOptions = ConfigurationOptions.Parse(TestDefaultConfiguration.RedisConnectionString!),
            GrainStorageSerializer = grainStorageSerializer,
            DeleteStateOnClear = deleteStateOnClear,
        };
        var connectTask = ConnectionMultiplexer.ConnectAsync(options.ConfigurationOptions);
        ConnectionMultiplexer connection;
        try
        {
            connection = await connectTask.WaitAsync(cancellationToken);
        }
        catch
        {
            _ = connectTask.ContinueWith(
                static task =>
                {
                    if (task.IsCompletedSuccessfully)
                    {
                        task.Result.Dispose();
                    }
                    else
                    {
                        _ = task.Exception;
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }

        options.CreateMultiplexer = _ => Task.FromResult((
            Multiplexer: (IConnectionMultiplexer)connection,
            IsShared: false));

        var clusterOptions = new ClusterOptions()
        {
            ServiceId = Guid.NewGuid().ToString()
        };

        var serviceProvider = DefaultProviderRuntime.ServiceProvider;
        var storageProvider = new RedisGrainStorage(
            string.Empty,
            options,
            grainStorageSerializer,
            Options.Create(clusterOptions),
            serviceProvider.GetRequiredService<IActivatorProvider>(),
            serviceProvider.GetRequiredService<ILogger<RedisGrainStorage>>());
        ISiloLifecycleSubject siloLifeCycle = new SiloLifecycleSubject(NullLoggerFactory.Instance.CreateLogger<SiloLifecycleSubject>());
        storageProvider.Participate(siloLifeCycle);
        try
        {
            await siloLifeCycle.OnStart(cancellationToken);
            return storageProvider;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
