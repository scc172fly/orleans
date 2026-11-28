using System;
using System.Threading.Tasks;
using Cassandra;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Messaging;

namespace Orleans.Clustering.Cassandra.Hosting;

/// <summary>
/// 将 Cassandra 配置为集群成员资格提供程序的扩展方法。
/// </summary>
public static class CassandraMembershipHostingExtensions
{
    /// <summary>
    /// 配置 Orleans 客户端使用 Cassandra 发现集群网关。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <returns>客户端构建器。</returns>
    public static IClientBuilder UseCassandraClustering(this IClientBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IGatewayListProvider, CassandraGatewayListProvider>();
        });

    /// <summary>
    /// 配置 Orleans 客户端使用 Cassandra 发现集群网关。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="sessionProvider">用于创建 <see cref="ISession"/> 的委托。</param>
    /// <returns>客户端构建器。</returns>
    public static IClientBuilder UseCassandraClustering(this IClientBuilder builder, Func<IServiceProvider, Task<ISession>> sessionProvider) =>
        builder.ConfigureServices(services =>
        {
            services.Configure<CassandraClusteringOptions>(o => o.ConfigureClient(sessionProvider));
            services.AddSingleton<IGatewayListProvider, CassandraGatewayListProvider>();
        });

    /// <summary>
    /// 配置 Orleans 客户端使用 Cassandra 发现集群网关。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="configureOptions">用于配置 Cassandra 客户端的委托。</param>
    /// <returns>客户端构建器。</returns>
    public static IClientBuilder UseCassandraClustering(this IClientBuilder builder, Action<CassandraClusteringOptions> configureOptions) =>
        builder.ConfigureServices(services =>
        {
            services.Configure<CassandraClusteringOptions>(configureOptions);
            services.AddSingleton<IGatewayListProvider, CassandraGatewayListProvider>();
        });

    /// <summary>
    /// 配置 Orleans 客户端使用 Cassandra 发现集群网关。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="connectionString">Cassandra 连接字符串。</param>
    /// <param name="keyspace">Cassandra 键空间，默认为 <c>orleans</c>。</param>
    /// <returns>客户端构建器。</returns>
    public static IClientBuilder UseCassandraClustering(this IClientBuilder builder, string connectionString, string keyspace = "orleans") =>
        builder.ConfigureServices(services =>
        {
            services.Configure<CassandraClusteringOptions>(o => o.ConfigureClient(connectionString, keyspace));
            services.AddSingleton<IGatewayListProvider, CassandraGatewayListProvider>();
        });

    /// <summary>
    /// 配置使用 Cassandra 的 Orleans 集群成员管理。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <returns>Silo 构建器。</returns>
    /// <remarks>从 DI 容器解析由 <see cref="IOptions{TOptions}"/> 包装的 <see cref="ClusterOptions"/> 选项及 <see cref="ISession"/>。</remarks>
    public static ISiloBuilder UseCassandraClustering(this ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IMembershipTable, CassandraClusteringTable>();
        });

    /// <summary>
    /// 配置使用 Cassandra 的 Orleans 集群成员管理。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="sessionProvider">用于创建 <see cref="ISession"/> 的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder UseCassandraClustering(this ISiloBuilder builder, Func<IServiceProvider, Task<ISession>> sessionProvider) =>
        builder.ConfigureServices(services =>
        {
            services.Configure<CassandraClusteringOptions>(o => o.ConfigureClient(sessionProvider));
            services.AddSingleton<IMembershipTable, CassandraClusteringTable>();
        });

    /// <summary>
    /// 配置使用 Cassandra 的 Orleans 集群成员管理。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configureOptions">用于配置 Cassandra 客户端的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder UseCassandraClustering(this ISiloBuilder builder, Action<CassandraClusteringOptions> configureOptions) =>
        builder.ConfigureServices(services =>
        {
            services.Configure<CassandraClusteringOptions>(configureOptions);
            services.AddSingleton<IMembershipTable, CassandraClusteringTable>();
        });

    /// <summary>
    /// 配置使用 Cassandra 的 Orleans 集群成员管理。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="connectionString">Cassandra 连接字符串。</param>
    /// <param name="keyspace">Cassandra 键空间，默认为 <c>orleans</c>。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder UseCassandraClustering(this ISiloBuilder builder, string connectionString, string keyspace = "orleans") =>
        builder.ConfigureServices(services =>
        {
            services.Configure<CassandraClusteringOptions>(o => o.ConfigureClient(connectionString, keyspace));
            services.AddSingleton<IMembershipTable, CassandraClusteringTable>();
        });
}
