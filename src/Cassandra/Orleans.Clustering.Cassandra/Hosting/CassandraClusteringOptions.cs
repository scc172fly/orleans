using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Cassandra;
using Orleans.Configuration;

namespace Orleans.Clustering.Cassandra.Hosting;

/// <summary>
/// 用于配置 Cassandra 集群成员管理的选项。
/// </summary>
public class CassandraClusteringOptions
{
    /// <summary>
    /// 获取或设置 Cassandra 是否在指定期限后使 Dead 成员资格记录过期：
    /// <see cref="ClusterMembershipOptions.DefunctSiloExpiration"/>.
    /// </summary>
    /// <remarks>
    /// 存活的成员资格记录和表版本保持持久化。对 Dead 记录的整行写入会将保留期限
    /// 同时应用于该行的全部字段。所有者心跳仅持久化心跳列。在 Dead 写入后到达的迟到心跳
    /// 可能在其余字段过期后留下仅含心跳的记录残片；成员资格读取
    /// 会在该行启动时间字段过期时将其移除。显式清理删除整行，同时保留
    /// 表版本。这适用于新表和现有表中的写入。
    /// </remarks>
    public bool UseCassandraTtl { get; set; }

    /// <summary>
    /// 指定初始化过程中遇到竞争时，
    /// 重试之前的最长等待时间。
    /// </summary>
    /// <remarks>通常仅在大量 Silo 在短时间内连接
    /// 且使用多数据中心 Cassandra 集群时才会遇到这种情况。</remarks>
    public TimeSpan InitializeRetryMaxDelay { get; set; } = TimeSpan.FromSeconds(20);

    internal int? GetCassandraTtlSeconds(ClusterMembershipOptions clusterMembershipOptions) =>
        UseCassandraTtl
            ? Convert.ToInt32(
                Math.Round(
                    clusterMembershipOptions.DefunctSiloExpiration.TotalSeconds,
                    MidpointRounding.AwayFromZero))
            : null;

    /// <summary>
    /// 配置 Cassandra 客户端。
    /// </summary>
    /// <param name="connectionString">连接字符串。</param>
    /// <param name="keyspace">键空间。</param>
    /// <remarks>成员资格提供程序拥有创建的集群，并在初始化取消或提供程序释放时释放该集群。</remarks>
    public void ConfigureClient(string connectionString, string keyspace = "orleans")
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentNullException.ThrowIfNull(keyspace);
        OwnsSession = true;
        CreateSessionAsync = async sp =>
        {
            var c = Cluster.Builder().WithConnectionString(connectionString)
                .Build();

            var connected = false;
            try
            {
                var session = await c.ConnectAsync(keyspace).ConfigureAwait(false);
                connected = true;
                return session;
            }
            finally
            {
                if (!connected)
                {
                    c.Dispose();
                }
            }
        };
    }

    /// <summary>
    /// 配置 Cassandra 客户端。
    /// </summary>
    /// <param name="configurationDelegate">连接字符串。</param>
    /// <remarks>此委托返回的会话仍由调用方所有，成员资格提供程序不会释放它们。</remarks>
    public void ConfigureClient(Func<IServiceProvider, Task<ISession>> configurationDelegate)
    {
        ArgumentNullException.ThrowIfNull(configurationDelegate);
        OwnsSession = false;
        CreateSessionAsync = configurationDelegate;
    }

    [NotNull]
    internal Func<IServiceProvider, Task<ISession>> CreateSessionAsync { get; private set; } = default!;

    internal bool OwnsSession { get; private set; }
}
