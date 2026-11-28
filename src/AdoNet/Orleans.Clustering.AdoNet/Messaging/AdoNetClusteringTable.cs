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
    /// 使用关系数据库实现 Orleans 集群成员资格。
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
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <param name="clusterOptions">集群标识选项。</param>
        /// <param name="clusteringOptions">关系数据库集群成员管理选项。</param>
        /// <param name="logger">日志记录器。</param>
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

            // 使用一个约定的视图从数据库初始化所有 Orleans 操作查询，
            // 并假定具有相应定义的数据库已经存在。
            orleansQueries = await RelationalOrleansQueries.CreateInstance(
                clusteringTableOptions.Invariant,
                clusteringTableOptions.ConnectionString,
                clusteringTableOptions.DataSource,
                cancellationToken);

            // 即使表不是由当前实例创建的，
            // 如果尚不存在初始表版本，也要尝试插入它，
            // 以确保在此 Silo 开始工作之前始终存在第一条表版本记录。
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

            // 插入行时应始终存在“tableVersion”参数，因为此时应已调用 Init，
            // 并创建及读取了成员表版本。这是一项优化，旨在
            // 避免一直访问到数据库层才因 ETag 条件检查失败（Orleans 成员资格协议要求此检查，
            // 以确保健壮性）。
            // 同样，不存在成员条目时也无法执行更新。
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

            // 更新行时应始终存在“tableVersion”参数，因为此时应已调用 Init，
            // 并创建及读取了成员表版本。这是一项优化，旨在
            // 避免一直访问到数据库层才因条件检查失败（Orleans 成员资格协议要求此检查，
            // 以确保健壮性）。
            // 同样，缺少成员条目或 ETag 时无法执行更新。
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

            // 关系数据库读取操作将表版本用作行 ETag。
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
