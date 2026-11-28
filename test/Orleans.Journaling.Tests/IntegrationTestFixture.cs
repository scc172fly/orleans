using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Journaling.Json;
using Orleans.TestingHost;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 使用 InProcessTestCluster 执行通用初始化的日志化测试基类
/// </summary>
public class IntegrationTestFixture : IAsyncLifetime
{
    public InProcessTestCluster Cluster { get; }
    public IClusterClient Client => Cluster.Client!; // 测试夹具初始化时部署客户端。

    public IntegrationTestFixture()
    {
        var builder = new InProcessTestClusterBuilder();
        var storageProvider = new VolatileJournalStorageProvider();
        builder.ConfigureSilo((options, siloBuilder) =>
        {
            siloBuilder.AddJournaling();
            siloBuilder.UseJsonJournalFormat(JournalingTestsJsonContext.Default);
            siloBuilder.Services.AddSingleton(storageProvider);
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(storageProvider);
        });
        ConfigureTestCluster(builder);
        Cluster = builder.Build();
    }

    protected virtual void ConfigureTestCluster(InProcessTestClusterBuilder builder)
    {
    }

    public virtual async ValueTask InitializeAsync()
    {
        await Cluster.DeployAsync();
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (Cluster != null)
        {
            await Cluster.DisposeAsync();
        }
    }
}

[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(TestDurableGrainState))]
[JsonSerializable(typeof(TestPerson))]
internal sealed partial class JournalingTestsJsonContext : JsonSerializerContext;
