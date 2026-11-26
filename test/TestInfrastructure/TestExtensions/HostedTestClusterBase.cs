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
    /// 用于确保 silo 集以默认配置启动的基准类,如果上次测试使用相同的默认基数,则避免重新启动。
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
