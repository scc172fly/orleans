using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Cassandra;
using Orleans.Configuration;

namespace Orleans.Clustering.Cassandra.Hosting;

/// <summary>
/// 配置 Cassandra 集群的选项 。
/// </summary>
public class CassandraClusteringOptions
{
    /// <summary>
    /// 确定卡珊德拉是否过期
    /// <see cref="ClusterMembershipOptions.DefunctSiloExpiration"/>.
    /// </summary>
    /// <remarks>
    /// 活成员行和表格版本仍保持持久性。 全行死亡写道指定保留
    /// 全部行字段的时长。所有所有心跳只持续到心跳列中。
    /// a 死书写完后,在剩余田场过期时,可留下只留下心跳片段;成员为
    /// 当开始时间过期时该行将退出该行。 明确清除会删除整列, 同时保存
    /// 表格版本。此选项适用于新表格和现有表格中的写法。
    /// </remarks>
    public bool UseCassandraTtl { get; set; }

    /// <summary>
    /// 指定遇到后等待的最大时间
    /// 重试前初始化时的争论。
    /// </summary>
    /// <remarks>通常只有在大量 silos 连接时才遇到这种情况
    /// 使用多数据中心卡桑德拉拉群集</remarks>
    public TimeSpan InitializeRetryMaxDelay { get; set; } = TimeSpan.FromSeconds(20);

    internal int? GetCassandraTtlSeconds(ClusterMembershipOptions clusterMembershipOptions) =>
        UseCassandraTtl
            ? Convert.ToInt32(
                Math.Round(
                    clusterMembershipOptions.DefunctSiloExpiration.TotalSeconds,
                    MidpointRounding.AwayFromZero))
            : null;

    /// <summary>
    /// 配置 Cassandra 客户端 。
    /// </summary>
    /// <param name="connectionString">连接字符串。</param>
    /// <param name="keyspace">密钥空间。</param>
    /// <remarks>成员提供商拥有所创建的集群,如果启动取消或供应商被处置,则予以处理。</remarks>
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
    /// 配置 Cassandra 客户端 。
    /// </summary>
    /// <param name="configurationDelegate">连接字符串。</param>
    /// <remarks>该代表的退席仍由打电话者所有,成员提供者不处理。</remarks>
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
