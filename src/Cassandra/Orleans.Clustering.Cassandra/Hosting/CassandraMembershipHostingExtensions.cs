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
/// 将卡桑德拉配置为集群供应商的推广方法。
/// </summary>
public static class CassandraMembershipHostingExtensions
{
    /// <summary>
    /// 配置 Orleans 组合,使用 Cassandra 。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <returns>客户端构建器。</returns>
    public static IClientBuilder UseCassandraClustering(this IClientBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IGatewayListProvider, CassandraGatewayListProvider>();
        });

    /// <summary>
    /// 配置 Orleans 组合,使用 Cassandra 。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="sessionProvider">代表用来创建 <see cref="ISession"/>。</param>
    /// <returns>客户端构建器。</returns>
    public static IClientBuilder UseCassandraClustering(this IClientBuilder builder, Func<IServiceProvider, Task<ISession>> sessionProvider) =>
        builder.ConfigureServices(services =>
        {
            services.Configure<CassandraClusteringOptions>(o => o.ConfigureClient(sessionProvider));
            services.AddSingleton<IGatewayListProvider, CassandraGatewayListProvider>();
        });

    /// <summary>
    /// 配置 Orleans 组合,使用 Cassandra 。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="configureOptions">用于配置 Cassandra 客户端的代表。</param>
    /// <returns>客户端构建器。</returns>
    public static IClientBuilder UseCassandraClustering(this IClientBuilder builder, Action<CassandraClusteringOptions> configureOptions) =>
        builder.ConfigureServices(services =>
        {
            services.Configure<CassandraClusteringOptions>(configureOptions);
            services.AddSingleton<IGatewayListProvider, CassandraGatewayListProvider>();
        });

    /// <summary>
    /// 配置 Orleans 组合,使用 Cassandra 。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="connectionString">Cassandra 连接字符串。</param>
    /// <param name="keyspace">卡桑德拉密钥空间,该空格默认为 <c>orleans</c>。</param>
    /// <returns>客户端构建器。</returns>
    public static IClientBuilder UseCassandraClustering(this IClientBuilder builder, string connectionString, string keyspace = "orleans") =>
        builder.ConfigureServices(services =>
        {
            services.Configure<CassandraClusteringOptions>(o => o.ConfigureClient(connectionString, keyspace));
            services.AddSingleton<IGatewayListProvider, CassandraGatewayListProvider>();
        });

    /// <summary>
    /// 配置 Orleans 组合,使用 Cassandra 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <returns>下 silo 建筑商</returns>
    /// <remarks>调压 <see cref="IOptions{TOptions}"/> 类型 <see cref="ClusterOptions"/> 和 <see cref="ISession"/> 数据交换容器中产生的</remarks>
    public static ISiloBuilder UseCassandraClustering(this ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IMembershipTable, CassandraClusteringTable>();
        });

    /// <summary>
    /// 配置 Orleans 组合,使用 Cassandra 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="sessionProvider">代表用来创建 <see cref="ISession"/>。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder UseCassandraClustering(this ISiloBuilder builder, Func<IServiceProvider, Task<ISession>> sessionProvider) =>
        builder.ConfigureServices(services =>
        {
            services.Configure<CassandraClusteringOptions>(o => o.ConfigureClient(sessionProvider));
            services.AddSingleton<IMembershipTable, CassandraClusteringTable>();
        });

    /// <summary>
    /// 配置 Orleans 组合,使用 Cassandra 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">用于配置 Cassandra 客户端的代表。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder UseCassandraClustering(this ISiloBuilder builder, Action<CassandraClusteringOptions> configureOptions) =>
        builder.ConfigureServices(services =>
        {
            services.Configure<CassandraClusteringOptions>(configureOptions);
            services.AddSingleton<IMembershipTable, CassandraClusteringTable>();
        });

    /// <summary>
    /// 配置 Orleans 组合,使用 Cassandra 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="connectionString">Cassandra 连接字符串。</param>
    /// <param name="keyspace">卡桑德拉密钥空间,该空格默认为 <c>orleans</c>。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder UseCassandraClustering(this ISiloBuilder builder, string connectionString, string keyspace = "orleans") =>
        builder.ConfigureServices(services =>
        {
            services.Configure<CassandraClusteringOptions>(o => o.ConfigureClient(connectionString, keyspace));
            services.AddSingleton<IMembershipTable, CassandraClusteringTable>();
        });
}
