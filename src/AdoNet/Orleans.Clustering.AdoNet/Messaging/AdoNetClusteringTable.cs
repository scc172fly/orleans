using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Clustering.AdoNet.Storage;
using Orleans.Configuration;

namespace Orleans.Runtime.MembershipService
{
    /// <summary>
    /// 利用关系数据库落实 Orleans 分组成员资格。
    /// </summary>
    public partial class AdoNetClusteringTable : IMembershipTable
    {
        private readonly string clusterId;
        private readonly IServiceProvider serviceProvider;
        private readonly ILogger logger;
        private RelationalOrleansQueries orleansQueries = null!;
        private readonly AdoNetClusteringSiloOptions clusteringTableOptions;

        /// <summary>
        /// 初始化 <see cref="AdoNetClusteringTable"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">服务供应商。</param>
        /// <param name="clusterOptions">集群识别选项。</param>
        /// <param name="clusteringOptions">关联组合组合选项。</param>
        /// <param name="logger">日志器。</param>
        public AdoNetClusteringTable(
            IServiceProvider serviceProvider,
            IOptions<ClusterOptions> clusterOptions,
            IOptions<AdoNetClusteringSiloOptions> clusteringOptions,
            ILogger<AdoNetClusteringTable> logger)
        {
            this.serviceProvider = serviceProvider;
            this.logger = logger;
            this.clusteringTableOptions = clusteringOptions.Value;
            this.clusterId = clusterOptions.Value.ClusterId;
        }

        /// <inheritdoc />
        [Obsolete("Use InitializeMembershipTableAsync instead.")]
        public Task InitializeMembershipTable(bool tryInitTableVersion) => InitializeMembershipTableAsync(tryInitTableVersion, CancellationToken.None);

        /// <inheritdoc />
        public async Task InitializeMembershipTableAsync(bool tryInitTableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceInitializeMembershipTable();

            //使用众所周知的视图, 启动数据库中所有 Orleans 操作查询
            //并假定数据库已经存在适当定义。
            orleansQueries = await RelationalOrleansQueries.CreateInstance(
                clusteringTableOptions.Invariant,
                clusteringTableOptions.ConnectionString,
                clusteringTableOptions.DataSource,
                cancellationToken);

            // 假若不是我创造了这个桌子,
            // 尝试插入初始表格版本,如果它尚未存在的话;
            // 因此我们总是有一个第一个表格版本行, 此时 silo 开始工作之前。
            if (tryInitTableVersion)
            {
                var wasCreated = await InitTableAsync(cancellationToken);
                if (wasCreated)
                {
                    LogInfoCreatedNewTableVersionRow();
                }
            }
        }

        /// <inheritdoc />
        [Obsolete("Use ReadAllAsync and MembershipTableData.TryGet instead.")]
        public Task<MembershipTableData> ReadRow(SiloAddress key) => ReadRowAsync(key, CancellationToken.None);

        /// <inheritdoc />
        [Obsolete("Use ReadAllAsync and MembershipTableData.TryGet instead.")]
        public Task<MembershipTableData> ReadRowAsync(SiloAddress key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<MembershipTableData>(new NotSupportedException("Use ReadAllAsync and MembershipTableData.TryGet instead."));
        }

        /// <inheritdoc />
        [Obsolete("Use ReadAllAsync instead.")]
        public Task<MembershipTableData> ReadAll() => ReadAllAsync(CancellationToken.None);

        /// <inheritdoc />
        public async Task<MembershipTableData> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceReadAll();
            try
            {
                return await orleansQueries.MembershipReadAllAsync(this.clusterId, cancellationToken);
            }
            catch (Exception ex)
            {
                LogDebugReadAllFailed(ex);
                throw;
            }
        }

        /// <inheritdoc />
        [Obsolete("Use InsertRowAsync instead.")]
        public Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion) => InsertRowAsync(entry, tableVersion, CancellationToken.None);

        /// <inheritdoc />
        public async Task<bool> InsertRowAsync(MembershipEntry entry, TableVersion tableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceInsertRow(entry, tableVersion);

            //在插入一行时, "tableVersion" 参数应始终与 Init 一样存在
            //已经被调用,会员的版本创建和阅读。
            //无法通过所有路径到数据库以失败 Etag 有条件检查( 它确实
            //成员议定书规定,为了稳健起见,存在 Orleans 成员资格议定书所规定的地位。
            //同样,没有加入,就不可能更新。
            if (entry == null)
            {
                LogDebugInsertRowAbortedNullEntry();
                throw new ArgumentNullException(nameof(entry));
            }
            if (tableVersion is null)
            {
                LogDebugInsertRowAbortedNullTableVersion();
                throw new ArgumentNullException(nameof(tableVersion));
            }

            try
            {
                return await orleansQueries.InsertMembershipRowAsync(this.clusterId, entry, tableVersion.VersionEtag, cancellationToken);
            }
            catch (Exception ex)
            {
                LogDebugInsertRowFailed(ex);
                throw;
            }
        }

        /// <inheritdoc />
        [Obsolete("Use UpdateRowAsync instead.")]
        public Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion) => UpdateRowAsync(entry, etag, tableVersion, CancellationToken.None);

        /// <inheritdoc />
        public async Task<bool> UpdateRowAsync(MembershipEntry entry, string etag, TableVersion tableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceUpdateRow(entry, etag, tableVersion);

            //"tableVersion" 参数在更新一行时应始终如 Init 那样存在
            //已经被调用,会员的版本创建和阅读。
            //无法通过任何路径到数据库以失败有条件检查( 它会
            //成员议定书规定,为了稳健起见,存在 Orleans 成员资格议定书所规定的地位。
            //同样,没有加入或加入,就不可能更新。
            if (entry == null)
            {
                LogDebugUpdateRowAbortedNullEntry();
                throw new ArgumentNullException(nameof(entry));
            }
            if (tableVersion is null)
            {
                LogDebugUpdateRowAbortedNullTableVersion();
                throw new ArgumentNullException(nameof(tableVersion));
            }

            // 关系读取时使用表格版本作为行 etag 。
            if (!string.Equals(etag, tableVersion.VersionEtag, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                return await orleansQueries.UpdateMembershipRowAsync(this.clusterId, entry, tableVersion.VersionEtag, cancellationToken);
            }
            catch (Exception ex)
            {
                LogDebugUpdateRowFailed(ex);
                throw;
            }
        }

        /// <inheritdoc />
        [Obsolete("Use UpdateIAmAliveAsync instead.")]
        public Task UpdateIAmAlive(MembershipEntry entry) => UpdateIAmAliveAsync(entry, CancellationToken.None);

        /// <inheritdoc />
        public async Task UpdateIAmAliveAsync(MembershipEntry entry, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceUpdateIAmAlive(entry);
            if (entry == null)
            {
                LogDebugUpdateIAmAliveAbortedNullEntry();
                throw new ArgumentNullException(nameof(entry));
            }
            try
            {
                await orleansQueries.UpdateIAmAliveTimeAsync(this.clusterId, entry.SiloAddress, entry.IAmAliveTime, cancellationToken);
            }
            catch (Exception ex)
            {
                LogDebugUpdateIAmAliveFailed(ex);
                throw;
            }
        }

        /// <inheritdoc />
        [Obsolete("Use DeleteMembershipTableEntriesAsync instead.")]
        public Task DeleteMembershipTableEntries(string clusterId) => DeleteMembershipTableEntriesAsync(clusterId, CancellationToken.None);

        /// <inheritdoc />
        public async Task DeleteMembershipTableEntriesAsync(string clusterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceDeleteMembershipTableEntries(clusterId);
            try
            {
                await orleansQueries.DeleteMembershipTableEntriesAsync(clusterId, cancellationToken);
            }
            catch (Exception ex)
            {
                LogDebugDeleteMembershipTableEntriesFailed(ex);
                throw;
            }
        }

        /// <inheritdoc />
        [Obsolete("Use CleanupDefunctSiloEntriesAsync instead.")]
        public Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate) => CleanupDefunctSiloEntriesAsync(beforeDate, CancellationToken.None);

        /// <inheritdoc />
        public async Task CleanupDefunctSiloEntriesAsync(DateTimeOffset beforeDate, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceCleanupDefunctSiloEntries(beforeDate, clusterId);
            try
            {
                await orleansQueries.CleanupDefunctSiloEntriesAsync(beforeDate, this.clusterId, cancellationToken);
            }
            catch (Exception ex)
            {
                LogDebugCleanupDefunctSiloEntriesFailed(ex);
                throw;
            }
        }

        private async Task<bool> InitTableAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await orleansQueries.InsertMembershipVersionRowAsync(this.clusterId, cancellationToken);
            }
            catch (Exception ex)
            {
                LogTraceInsertSiloMembershipVersionFailed(ex);
                throw;
            }
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(InitializeMembershipTableAsync)} called."
        )]
        private partial void LogTraceInitializeMembershipTable();

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Created new table version row."
        )]
        private partial void LogInfoCreatedNewTableVersionRow();

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(ReadRowAsync)} called with key: {{Key}}."
        )]
        private partial void LogTraceReadRow(SiloAddress key);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(ReadRowAsync)} failed"
        )]
        private partial void LogDebugReadRowFailed(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(ReadAllAsync)} called."
        )]
        private partial void LogTraceReadAll();

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(ReadAllAsync)} failed"
        )]
        private partial void LogDebugReadAllFailed(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(InsertRowAsync)} called with entry {{Entry}} and tableVersion {{TableVersion}}."
        )]
        private partial void LogTraceInsertRow(MembershipEntry entry, TableVersion tableVersion);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(InsertRowAsync)} aborted due to null check. MembershipEntry is null."
        )]
        private partial void LogDebugInsertRowAbortedNullEntry();

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(InsertRowAsync)} aborted due to null check. TableVersion is null "
        )]
        private partial void LogDebugInsertRowAbortedNullTableVersion();

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(InsertRowAsync)} failed"
        )]
        private partial void LogDebugInsertRowFailed(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = $"{nameof(IMembershipTable)}.{nameof(UpdateRowAsync)} called with entry {{Entry}}, etag {{ETag}} and tableVersion {{TableVersion}}."
        )]
        private partial void LogTraceUpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(UpdateRowAsync)} aborted due to null check. MembershipEntry is null."
        )]
        private partial void LogDebugUpdateRowAbortedNullEntry();

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(UpdateRowAsync)} aborted due to null check. TableVersion is null"
        )]
        private partial void LogDebugUpdateRowAbortedNullTableVersion();

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(UpdateRowAsync)} failed"
        )]
        private partial void LogDebugUpdateRowFailed(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = $"{nameof(IMembershipTable)}.{nameof(UpdateIAmAliveAsync)} called with entry {{Entry}}."
        )]
        private partial void LogTraceUpdateIAmAlive(MembershipEntry entry);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(UpdateIAmAliveAsync)} aborted due to null check. MembershipEntry is null."
        )]
        private partial void LogDebugUpdateIAmAliveAbortedNullEntry();

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(UpdateIAmAliveAsync)} failed"
        )]
        private partial void LogDebugUpdateIAmAliveFailed(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = $"{nameof(IMembershipTable)}.{nameof(DeleteMembershipTableEntriesAsync)} called with clusterId {{ClusterId}}."
        )]
        private partial void LogTraceDeleteMembershipTableEntries(string clusterId);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(DeleteMembershipTableEntriesAsync)} failed"
        )]
        private partial void LogDebugDeleteMembershipTableEntriesFailed(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = $"{nameof(IMembershipTable)}.{nameof(CleanupDefunctSiloEntriesAsync)} called with beforeDate {{beforeDate}} and clusterId {{ClusterId}}."
        )]
        private partial void LogTraceCleanupDefunctSiloEntries(DateTimeOffset beforeDate, string clusterId);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(AdoNetClusteringTable)}.{nameof(CleanupDefunctSiloEntriesAsync)} failed"
        )]
        private partial void LogDebugCleanupDefunctSiloEntriesFailed(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Insert silo membership version failed"
        )]
        private partial void LogTraceInsertSiloMembershipVersionFailed(Exception exception);
    }
}
