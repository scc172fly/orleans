using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Serialization;
using Orleans.TestingHost;
using Xunit;

namespace TestExtensions
{
    /// <summary>
    /// 基类，确保使用默认配置启动 Silo 集群；如果上一个测试使用同一默认基类，则避免重启集群。
    /// </summary>
    [Collection("DefaultCluster")]
    public abstract class HostedTestClusterEnsureDefaultStarted : OrleansTestingBase
    {
        protected DefaultClusterFixture Fixture { get; private set; }
        protected TestCluster HostedCluster => this.Fixture.HostedCluster;

        protected IGrainFactory GrainFactory => this.HostedCluster.GrainFactory;

        protected IClusterClient Client => this.HostedCluster.Client;
        protected ILogger Logger => this.Client.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Application");

        protected HostedTestClusterEnsureDefaultStarted(DefaultClusterFixture fixture)
        {
            this.Fixture = fixture;
        }
    }

    public static class TestClusterExtensions
    {
        [return: MaybeNull]
        public static T RoundTripSystemTextJsonSerialization<T>(this TestCluster cluster, [AllowNull] T value)
        {
            var serialized = JsonSerializer.Serialize<T>(value!);
            return JsonSerializer.Deserialize<T>(serialized);
        }

        [return: MaybeNull]
        public static T RoundTripSerializationForTesting<T>(this TestCluster cluster, [AllowNull] T value)
        {
            var serializer = cluster.ServiceProvider.GetRequiredService<Serializer>();
            return serializer.Deserialize<T>(serializer.SerializeToArray(value));
        }

        [return: MaybeNull]
        public static T DeepCopy<T>(this TestCluster cluster, [AllowNull] T value)
        {
            var copier = cluster.ServiceProvider.GetRequiredService<DeepCopier>();
            return copier.Copy(value);
        }

        public static Serializer GetSerializer(this TestCluster cluster)
        {
            return cluster.ServiceProvider.GetRequiredService<Serializer>();
        }
    }
}
