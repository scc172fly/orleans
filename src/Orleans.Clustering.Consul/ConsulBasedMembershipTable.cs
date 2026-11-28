using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Consul;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime.Host;

namespace Orleans.Runtime.Membership
{
    /// <summary>
    /// 使用 Consul 0.6.0 的成员资格表实现，https://consul.io/
    /// </summary>
    public partial class ConsulBasedMembershipTable : IMembershipTable
    {
        private static readonly TableVersion NotFoundTableVersion = new TableVersion(0, "0");
        private static readonly QueryOptions ConsistentRead = new() { Consistency = ConsistencyMode.Consistent };
        private static readonly TimeSpan ConflictRetryDelay = TimeSpan.FromMilliseconds(100);
        private readonly ILogger _logger;
        private readonly IConsulClient _consulClient;
        private readonly ConsulClusteringOptions clusteringSiloTableOptions;
        private readonly string clusterId;
        private readonly string? kvRootFolder;
        private readonly string versionKey;

        /// <summary>
        /// 初始化 <see cref="ConsulBasedMembershipTable"/> 类的新实例。
        /// </summary>
        /// <param name="logger">日志记录器。</param>
        /// <param name="membershipTableOptions">Consul 集群成员管理选项。</param>
        /// <param name="clusterOptions">集群标识选项。</param>
        public ConsulBasedMembershipTable(
            ILogger<ConsulBasedMembershipTable> logger,
            IOptions<ConsulClusteringOptions> membershipTableOptions,
            IOptions<ClusterOptions> clusterOptions)
        {
            ArgumentNullException.ThrowIfNull(clusterOptions);
            ArgumentNullException.ThrowIfNull(membershipTableOptions);
            this.clusterId = clusterOptions.Value.ClusterId;
            this.kvRootFolder = membershipTableOptions.Value.KvRootFolder;
            this._logger = logger;
            this.clusteringSiloTableOptions = membershipTableOptions.Value;
            this._consulClient = this.clusteringSiloTableOptions.CreateClient();
            versionKey = ConsulSiloRegistrationAssembler.FormatVersionKey(clusterId, kvRootFolder);
        }

        /// <summary>
        /// 初始化基于 Consul 的成员资格表。
        /// </summary>
        /// <param name="tryInitTableVersion">当初始表版本不存在时是否创建它。</param>
        /// <returns></returns>
        [Obsolete("Use InitializeMembershipTableAsync instead.")]
        public Task InitializeMembershipTable(bool tryInitTableVersion) => InitializeMembershipTableAsync(tryInitTableVersion, CancellationToken.None);

        /// <inheritdoc />
        public async Task InitializeMembershipTableAsync(bool tryInitTableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tryInitTableVersion)
            {
                var response = await _consulClient.KV.CAS(new KVPair(versionKey)
                {
                    ModifyIndex = 0,
                    Value = Encoding.UTF8.GetBytes("0")
                }, cancellationToken);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new OrleansException($"Consul failed to initialize the membership table: {response.StatusCode}.");
                }
            }
        }

        /// <inheritdoc />
        [Obsolete("Use ReadAllAsync and MembershipTableData.TryGet instead.")]
        public Task<MembershipTableData> ReadRow(SiloAddress siloAddress) => ReadRowAsync(siloAddress, CancellationToken.None);

        /// <inheritdoc />
        [Obsolete("Use ReadAllAsync and MembershipTableData.TryGet instead.")]
        public Task<MembershipTableData> ReadRowAsync(SiloAddress siloAddress, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<MembershipTableData>(new NotSupportedException("Use ReadAllAsync and MembershipTableData.TryGet instead."));
        }

        /// <inheritdoc />
        [Obsolete("Use ReadAllAsync instead.")]
        public Task<MembershipTableData> ReadAll() => ReadAllAsync(CancellationToken.None);

        /// <inheritdoc />
        public Task<MembershipTableData> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            return ReadAllAsync(this._consulClient, this.clusterId, this.kvRootFolder, this._logger, this.versionKey, cancellationToken);
        }

        /// <summary>
        /// 从 Consul 读取集群的所有成员资格记录。
        /// </summary>
        /// <param name="consulClient">Consul 客户端。</param>
        /// <param name="clusterId">集群标识符。</param>
        /// <param name="kvRootFolder">包含 Orleans 键的可选根目录。</param>
        /// <param name="logger">日志记录器。</param>
        /// <param name="versionKey">包含成员资格表版本的键；为 <see langword="null"/> 时使用集群默认版本键。</param>
        /// <returns>集群成员资格记录和表版本。</returns>
        [Obsolete("Use ReadAllAsync instead.")]
        public static Task<MembershipTableData> ReadAll(IConsulClient consulClient, string clusterId, string? kvRootFolder, ILogger logger, string? versionKey) =>
            ReadAllAsync(consulClient, clusterId, kvRootFolder, logger, versionKey, CancellationToken.None);

        /// <summary>
        /// 从 Consul 读取集群的所有成员资格记录。
        /// </summary>
        /// <param name="consulClient">Consul 客户端。</param>
        /// <param name="clusterId">集群标识符。</param>
        /// <param name="kvRootFolder">包含 Orleans 键的可选根目录。</param>
        /// <param name="logger">日志记录器。</param>
        /// <param name="versionKey">包含成员资格表版本的键；为 <see langword="null"/> 时使用集群默认版本键。</param>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        /// <returns>集群成员资格记录和表版本。</returns>
        public static async Task<MembershipTableData> ReadAllAsync(IConsulClient consulClient, string clusterId, string? kvRootFolder, ILogger logger, string? versionKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(consulClient);
            var deploymentKVAddresses = await consulClient.KV.List(ConsulSiloRegistrationAssembler.FormatDeploymentKVPrefix(clusterId, kvRootFolder) + "/", ConsistentRead, cancellationToken);
            if (deploymentKVAddresses.Response == null)
            {
                LogDebugCouldNotFindSiloRegistrations(logger, clusterId);
                return new MembershipTableData(NotFoundTableVersion);
            }

            var allSiloRegistrations =
                GetSiloEntries(deploymentKVAddresses.Response)
                .Select(entry => ConsulSiloRegistrationAssembler.FromKVPairs(clusterId, entry.Row, entry.Heartbeat))
                .ToArray();

            var tableVersionKey = versionKey ?? ConsulSiloRegistrationAssembler.FormatVersionKey(clusterId, kvRootFolder);
            var tableVersion = GetTableVersion(deploymentKVAddresses.Response.SingleOrDefault(kv => kv.Key.Equals(tableVersionKey, StringComparison.Ordinal)));

            return AssembleMembershipTableData(tableVersion, allSiloRegistrations);
        }

        /// <inheritdoc />
        [Obsolete("Use InsertRowAsync instead.")]
        public Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion) => InsertRowAsync(entry, tableVersion, CancellationToken.None);

        /// <inheritdoc />
        public async Task<bool> InsertRowAsync(MembershipEntry entry, TableVersion tableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(tableVersion);
            try
            {
                // Consul 将 CAS 索引为零的操作视为插入。
                var siloRegistration = ConsulSiloRegistrationAssembler.FromMembershipEntry(this.clusterId, entry, "0");
                var insertKV = ConsulSiloRegistrationAssembler.ToKVPair(siloRegistration, this.kvRootFolder);
                var rowInsert = new KVTxnOp(insertKV.Key, KVTxnVerb.CAS) { Index = siloRegistration.LastIndex, Value = insertKV.Value };
                var versionUpdate = this.GetVersionRowUpdate(tableVersion);
                var responses = await _consulClient.KV.Txn(new List<KVTxnOp> { rowInsert, versionUpdate }, cancellationToken);
                if (!IsTransactionSuccessful(responses.Response))
                {
                    LogDebugConsulMembershipProviderFailedToInsertRow(entry.SiloAddress);
                    return false;
                }

                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogInformationConsulMembershipProviderFailedToInsertRegistration(ex, entry.SiloAddress);
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
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(tableVersion);
            try
            {
                var expectedIndex = ulong.Parse(etag, CultureInfo.InvariantCulture);
                // 在 Consul 中，索引零表示插入；更新需要现有记录的令牌。
                if (expectedIndex == 0)
                {
                    LogDebugConsulMembershipProviderFailedCASCheck(entry.SiloAddress);
                    return false;
                }

                var siloRegistration = ConsulSiloRegistrationAssembler.FromMembershipEntry(this.clusterId, entry, etag);
                var updateKV = ConsulSiloRegistrationAssembler.ToKVPair(siloRegistration, this.kvRootFolder);
                var operations = new List<KVTxnOp>
                {
                    new(updateKV.Key, KVTxnVerb.CAS) { Index = expectedIndex, Value = updateKV.Value },
                    GetVersionRowUpdate(tableVersion)
                };
                var response = await _consulClient.KV.Txn(operations, cancellationToken);
                if (!IsTransactionSuccessful(response.Response))
                {
                    LogDebugConsulMembershipProviderFailedCASCheck(entry.SiloAddress);
                    return false;
                }

                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogInformationConsulMembershipProviderFailedToUpdateRegistration(ex, entry.SiloAddress);
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
            ArgumentNullException.ThrowIfNull(entry);
            var heartbeat = ConsulSiloRegistrationAssembler.ToIAmAliveKVPair(clusterId, kvRootFolder, entry.SiloAddress, entry.IAmAliveTime);
            var response = await _consulClient.KV.Put(heartbeat, cancellationToken);
            if (!response.Response)
            {
                throw new OrleansException($"Consul failed to update the heartbeat for silo '{entry.SiloAddress}'.");
            }
        }

        /// <inheritdoc />
        [Obsolete("Use DeleteMembershipTableEntriesAsync instead.")]
        public Task DeleteMembershipTableEntries(string clusterId) => DeleteMembershipTableEntriesAsync(clusterId, CancellationToken.None);

        /// <inheritdoc />
        public async Task DeleteMembershipTableEntriesAsync(string clusterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await _consulClient.KV.DeleteTree(ConsulSiloRegistrationAssembler.FormatDeploymentKVPrefix(clusterId, this.kvRootFolder) + "/", cancellationToken);
            if (!response.Response)
            {
                throw new OrleansException($"Consul failed to delete membership entries for cluster '{clusterId}'.");
            }
        }

        private static TableVersion GetTableVersion(KVPair? tableVersionEntry)
        {
            TableVersion tableVersion;
            if (tableVersionEntry != null)
            {
                var versionNumber = int.Parse(Encoding.UTF8.GetString(tableVersionEntry.Value), CultureInfo.InvariantCulture);
                tableVersion = new TableVersion(versionNumber, tableVersionEntry.ModifyIndex.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                tableVersion = NotFoundTableVersion;
            }

            return tableVersion;
        }

        private KVTxnOp GetVersionRowUpdate(TableVersion version)
        {
            var index = ulong.Parse(version.VersionEtag, CultureInfo.InvariantCulture);
            var versionBytes = Encoding.UTF8.GetBytes(version.Version.ToString(CultureInfo.InvariantCulture));
            return new KVTxnOp(this.versionKey, KVTxnVerb.CAS) { Index = index, Value = versionBytes };
        }

        private static bool IsTransactionSuccessful(KVTxnResponse response)
        {
            if (response.Success)
            {
                return true;
            }

            // Consul 对索引过期冲突以及 ACL 拒绝等单项操作失败均返回 HTTP 409。
            if (response.Errors.Count > 0 && response.Errors.All(error =>
                (error.What.StartsWith("failed to set key ", StringComparison.Ordinal)
                    || error.What.StartsWith("failed to delete key ", StringComparison.Ordinal))
                && error.What.EndsWith(", index is stale", StringComparison.Ordinal)))
            {
                return false;
            }

            var details = response.Errors.Count == 0
                ? "Consul returned no error details."
                : string.Join("; ", response.Errors.Select(error => $"Operation {error.OpIndex}: {error.What}"));
            throw new OrleansException($"Consul membership transaction failed: {details}");
        }

        private static IEnumerable<(KVPair Row, KVPair? Heartbeat)> GetSiloEntries(KVPair[] entries)
        {
            var entriesByKey = entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
            foreach (var row in entries)
            {
                if (row.Key.EndsWith("/" + ConsulSiloRegistrationAssembler.SiloIAmAliveSuffix, StringComparison.Ordinal)
                    || row.Key.EndsWith("/" + ConsulSiloRegistrationAssembler.VersionSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                entriesByKey.TryGetValue(ConsulSiloRegistrationAssembler.FormatSiloIAmAliveKey(row.Key), out var heartbeat);
                yield return (row, heartbeat);
            }
        }

        private async Task<(ConsulSiloRegistration?, TableVersion)> GetConsulSiloRegistration(SiloAddress siloAddress, CancellationToken cancellationToken)
        {
            var siloKey = ConsulSiloRegistrationAssembler.FormatDeploymentSiloKey(this.clusterId, this.kvRootFolder, siloAddress);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var versionBefore = (await _consulClient.KV.Get(versionKey, ConsistentRead, cancellationToken)).Response;
                var entries = (await _consulClient.KV.List(siloKey, ConsistentRead, cancellationToken)).Response;
                var versionAfter = (await _consulClient.KV.Get(versionKey, ConsistentRead, cancellationToken)).Response;

                // 版本 ETag 未改变，说明记录与心跳的原子读取发生在此表版本内。
                if (versionBefore?.ModifyIndex != versionAfter?.ModifyIndex)
                {
                    await Task.Delay(ConflictRetryDelay, cancellationToken);
                    continue;
                }

                var siloKV = entries?.SingleOrDefault(kv => kv.Key.Equals(siloKey, StringComparison.Ordinal));
                var iAmAliveKV = entries?.SingleOrDefault(kv => kv.Key.Equals(ConsulSiloRegistrationAssembler.FormatSiloIAmAliveKey(siloKey), StringComparison.Ordinal));
                var siloRegistration = siloKV is null ? null : ConsulSiloRegistrationAssembler.FromKVPairs(this.clusterId, siloKV, iAmAliveKV);

                return (siloRegistration, GetTableVersion(versionAfter));
            }
        }

        private static MembershipTableData AssembleMembershipTableData(TableVersion tableVersion, params ConsulSiloRegistration?[] silos)
        {
            var membershipEntries = silos
                .OfType<ConsulSiloRegistration>()
                .Select(silo => ConsulSiloRegistrationAssembler.ToMembershipEntry(silo))
                .ToList();

            return new MembershipTableData(membershipEntries, tableVersion);
        }

        /// <inheritdoc />
        [Obsolete("Use CleanupDefunctSiloEntriesAsync instead.")]
        public Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate) => CleanupDefunctSiloEntriesAsync(beforeDate, CancellationToken.None);

        /// <inheritdoc />
        public async Task CleanupDefunctSiloEntriesAsync(DateTimeOffset beforeDate, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var allKVs = await _consulClient.KV.List(ConsulSiloRegistrationAssembler.FormatDeploymentKVPrefix(this.clusterId, this.kvRootFolder) + "/", ConsistentRead, cancellationToken);
            if (allKVs.Response == null)
            {
                LogDebugCouldNotFindSiloRegistrationsForCleanup(this.clusterId);
                return;
            }

            var allRegistrations =
                GetSiloEntries(allKVs.Response)
                .Select(entry =>
                {
                    return new
                    {
                        RegistrationKey = entry.Row.Key,
                        entry.Heartbeat,
                        Registration = ConsulSiloRegistrationAssembler.FromKVPairs(clusterId, entry.Row, entry.Heartbeat)
                    };
                }).ToArray();

            foreach (var entry in allRegistrations)
            {
                if (entry.Registration.Status == SiloStatus.Dead
                    && Math.Max(entry.Registration.IAmAliveTime.Ticks, entry.Registration.StartTime.Ticks) < beforeDate.UtcDateTime.Ticks
                    && entry.Registration.SuspectingSilos?.Any(vote => vote.Time >= beforeDate.UtcDateTime) != true)
                {
                    var response = await _consulClient.KV.Txn(new List<KVTxnOp>
                    {
                        new(entry.RegistrationKey, KVTxnVerb.DeleteCAS) { Index = entry.Registration.LastIndex },
                        // 索引为零的 DeleteCAS 用于保证旧式心跳键不存在。
                        new(ConsulSiloRegistrationAssembler.FormatSiloIAmAliveKey(entry.RegistrationKey), KVTxnVerb.DeleteCAS) { Index = entry.Heartbeat?.ModifyIndex ?? 0 }
                    }, cancellationToken);
                    if (!IsTransactionSuccessful(response.Response))
                    {
                        LogDebugConsulMembershipProviderFailedCASCheck(entry.Registration.Address);
                    }
                }
            }
        }

        [LoggerMessage(
            Level = Microsoft.Extensions.Logging.LogLevel.Debug,
            Message = "Could not find any silo registrations for deployment {ClusterId}."
        )]
        private static partial void LogDebugCouldNotFindSiloRegistrations(ILogger logger, string clusterId);

        [LoggerMessage(
            Level = Microsoft.Extensions.Logging.LogLevel.Debug,
            Message = "ConsulMembershipProvider failed to insert the row {SiloAddress}."
        )]
        private partial void LogDebugConsulMembershipProviderFailedToInsertRow(SiloAddress siloAddress);

        [LoggerMessage(
            Level = Microsoft.Extensions.Logging.LogLevel.Information,
            Message = "ConsulMembershipProvider failed to insert registration for silo {SiloAddress}"
        )]
        private partial void LogInformationConsulMembershipProviderFailedToInsertRegistration(Exception ex, SiloAddress siloAddress);

        [LoggerMessage(
            Level = Microsoft.Extensions.Logging.LogLevel.Debug,
            Message = "ConsulMembershipProvider failed the CAS check when updating the registration for silo {SiloAddress}."
        )]
        private partial void LogDebugConsulMembershipProviderFailedCASCheck(SiloAddress siloAddress);

        [LoggerMessage(
            Level = Microsoft.Extensions.Logging.LogLevel.Information,
            Message = "ConsulMembershipProvider failed to update the registration for silo {SiloAddress}"
        )]
        private partial void LogInformationConsulMembershipProviderFailedToUpdateRegistration(Exception ex, SiloAddress siloAddress);

        [LoggerMessage(
            Level = Microsoft.Extensions.Logging.LogLevel.Debug,
            Message = "Could not find any silo registrations for deployment {ClusterId}."
        )]
        private partial void LogDebugCouldNotFindSiloRegistrationsForCleanup(string clusterId);
    }
}
