using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using StackExchange.Redis;
using Orleans.Configuration;
using Newtonsoft.Json;
using System.Linq;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Clustering.Redis
{
    /// <summary>
    /// 从 Redis 主节点读取成员资格快照，并在主节点上执行成员资格写入脚本。
    /// </summary>
    internal class RedisMembershipTable : IMembershipTable, IDisposable, IAsyncDisposable
    {
        private const string TableVersionKey = "Version";
        private static readonly TableVersion DefaultTableVersion = new TableVersion(0, "0");
        private readonly RedisClusteringOptions _redisOptions;
        private readonly ClusterOptions _clusterOptions;
        private readonly JsonSerializerSettings _jsonSerializerSettings;
        private readonly RedisKey _clusterKey;
        private readonly object _lifecycleLock = new();
        private readonly SemaphoreSlim _initializationLock = new(1, 1);
        private IConnectionMultiplexer _muxer = null!;
        private IDatabase _db = null!;
        private bool _muxerIsShared;
        private bool _disposed;
        private int _initializingCount;
        private IConnectionMultiplexer? _deferredOwnedMultiplexer;
        private TaskCompletionSource? _deferredDisposal;

        public RedisMembershipTable(IOptions<RedisClusteringOptions> redisOptions, IOptions<ClusterOptions> clusterOptions)
        {
            _redisOptions = redisOptions.Value;
            _clusterOptions = clusterOptions.Value;
            _clusterKey = _redisOptions.CreateRedisKey(_clusterOptions);
            _jsonSerializerSettings = JsonSettings.JsonSerializerSettings;
        }

        public bool IsInitialized { get; private set; }

        [Obsolete("Use DeleteMembershipTableEntriesAsync instead.")]
        public Task DeleteMembershipTableEntries(string clusterId) => DeleteMembershipTableEntriesAsync(clusterId, CancellationToken.None);

        public async Task DeleteMembershipTableEntriesAsync(string clusterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(clusterId, _clusterOptions.ClusterId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Cluster id {clusterId} does not match RedisMembershipTable value of '{_clusterOptions.ClusterId}'.",
                    nameof(clusterId));
            }

            await AwaitAsync(_db.KeyDeleteAsync(_clusterKey), cancellationToken);
        }

        [Obsolete("Use InitializeMembershipTableAsync instead.")]
        public Task InitializeMembershipTable(bool tryInitTableVersion) => InitializeMembershipTableAsync(tryInitTableVersion, CancellationToken.None);

        public async Task InitializeMembershipTableAsync(bool tryInitTableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                // 将等待信号量的操作也纳入统计，确保在所有等待及释放操作完成之后才进行资源释放。
                _initializingCount++;
            }

            try
            {
                await _initializationLock.WaitAsync(cancellationToken);
                try
                {
                    await InitializeMembershipTableCoreAsync(tryInitTableVersion, cancellationToken);
                }
                finally
                {
                    _initializationLock.Release();
                }
            }
            finally
            {
                IConnectionMultiplexer? deferredMultiplexer = null;
                TaskCompletionSource? deferredDisposal = null;
                lock (_lifecycleLock)
                {
                    if (--_initializingCount == 0 && _disposed)
                    {
                        _initializationLock.Dispose();
                        deferredMultiplexer = _deferredOwnedMultiplexer;
                        _deferredOwnedMultiplexer = null;
                        deferredDisposal = _deferredDisposal;
                    }
                }

                if (deferredMultiplexer is not null)
                {
                    try
                    {
                        await deferredMultiplexer.DisposeAsync().ConfigureAwait(false);
                        deferredDisposal!.SetResult();
                    }
                    catch (Exception exception)
                    {
                        deferredDisposal!.SetException(exception);
                        throw;
                    }
                }
            }
        }

        private async Task InitializeMembershipTableCoreAsync(bool tryInitTableVersion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IConnectionMultiplexer? muxer;
            bool isShared;
            lock (_lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                muxer = _muxer;
                isShared = _muxerIsShared;
            }

            var isNewConnection = muxer is null;
            var initialized = false;
            try
            {
                if (muxer is null)
                {
                    var creation = _redisOptions.CreateMultiplexer(_redisOptions);
                    try
                    {
                        (muxer, isShared) = await AwaitAsync(creation, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // 不接受取消令牌的工厂仍可能在调用方停止等待后返回由提供程序拥有的连接。
                        DisposeAbandonedMultiplexerAsync(creation).Ignore();
                        throw;
                    }
                }

                IDatabase db;
                lock (_lifecycleLock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    db = isNewConnection ? muxer.GetDatabase() : _db;
                }

                if (tryInitTableVersion)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await AwaitAsync(db.HashSetAsync(_clusterKey, TableVersionKey, SerializeVersion(DefaultTableVersion), When.NotExists), cancellationToken);

                    if (_redisOptions.EntryExpiry is { } expiry)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await AwaitAsync(db.KeyExpireAsync(_clusterKey, expiry), cancellationToken);
                    }
                }

                lock (_lifecycleLock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (isNewConnection)
                    {
                        _muxer = muxer;
                        _muxerIsShared = isShared;
                        _db = db;
                    }

                    IsInitialized = true;
                    initialized = true;
                }
            }
            finally
            {
                if (!initialized && isNewConnection && !isShared && muxer is not null)
                {
                    await muxer.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task DisposeAbandonedMultiplexerAsync(Task<(IConnectionMultiplexer Multiplexer, bool IsShared)> creation)
        {
            var (muxer, isShared) = await creation.ConfigureAwait(false);
            if (!isShared)
            {
                await muxer.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Obsolete("Use InsertRowAsync instead.")]
        public Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion) => InsertRowAsync(entry, tableVersion, CancellationToken.None);

        public async Task<bool> InsertRowAsync(MembershipEntry entry, TableVersion tableVersion, CancellationToken cancellationToken = default)
        {
            return await UpsertRowInternal(entry, tableVersion, allowInsertOnly: true, cancellationToken);
        }

        private async Task<bool> UpsertRowInternal(MembershipEntry entry, TableVersion tableVersion, bool allowInsertOnly, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 服务端令牌验证可避免对同一哈希表的心跳写入引起 WATCH 冲突。
            const string script =
                """
                if redis.call('HGET', KEYS[1], 'Version') ~= ARGV[2]
                    or tonumber(ARGV[2]) ~= tonumber(ARGV[3]) - 1 then
                    return 0
                end
                if redis.call('HEXISTS', KEYS[1], ARGV[1]) ~= tonumber(ARGV[5]) then
                    return 0
                end
                redis.call('HSET', KEYS[1], 'Version', ARGV[3], ARGV[1], ARGV[4])
                return 1
                """;
            var result = await AwaitAsync(_db.ScriptEvaluateAsync(
                script,
                [_clusterKey],
                [entry.SiloAddress.ToString(), tableVersion.VersionEtag, SerializeVersion(tableVersion), Serialize(entry), allowInsertOnly ? 0 : 1],
                CommandFlags.DemandMaster | CommandFlags.NoScriptCache), cancellationToken);
            return (int)result == 1;
        }

        [Obsolete("Use ReadAllAsync instead.")]
        public Task<MembershipTableData> ReadAll() => ReadAllAsync(CancellationToken.None);

        public async Task<MembershipTableData> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var all = await AwaitAsync(_db.HashGetAllAsync(_clusterKey, CommandFlags.DemandMaster), cancellationToken);
            var tableVersionRow = all.SingleOrDefault(h => TableVersionKey.Equals(h.Name, StringComparison.Ordinal));
            TableVersion tableVersion = GetTableVersionFromRow(tableVersionRow.Value);

            var data = all.Where(x => !TableVersionKey.Equals(x.Name, StringComparison.Ordinal) && x.Value.HasValue)
                .Select(x => Tuple.Create(Deserialize(x.Value!), tableVersion.VersionEtag))
                .ToList();
            return new MembershipTableData(data, tableVersion);
        }

        private static TableVersion GetTableVersionFromRow(RedisValue tableVersionRow)
        {
            if (TryGetValueString(tableVersionRow, out var value))
            {
                return DeserializeVersion(value);
            }

            throw new RedisClusteringException("The Redis membership table version is missing. The table may have expired or been deleted.");
        }

        private static bool TryGetValueString(RedisValue key, [NotNullWhen(true)] out string? value)
        {
            if (key.HasValue)
            {
                value = key.ToString();
                return true;
            }

            value = null;
            return false;
        }

        [Obsolete("Use ReadAllAsync and MembershipTableData.TryGet instead.")]
        public Task<MembershipTableData> ReadRow(SiloAddress key) => ReadRowAsync(key, CancellationToken.None);

        [Obsolete("Use ReadAllAsync and MembershipTableData.TryGet instead.")]
        public Task<MembershipTableData> ReadRowAsync(SiloAddress key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<MembershipTableData>(new NotSupportedException("Use ReadAllAsync and MembershipTableData.TryGet instead."));
        }

        [Obsolete("Use UpdateIAmAliveAsync instead.")]
        public Task UpdateIAmAlive(MembershipEntry entry) => UpdateIAmAliveAsync(entry, CancellationToken.None);

        public async Task UpdateIAmAliveAsync(MembershipEntry entry, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // JsonSettings 始终生成此 ISO 格式时间戳。直接修改其字节，以原样保留其他所有字段。
            const string script =
                """
                local row = redis.call('HGET', KEYS[1], ARGV[1])
                local first, last = string.find(row, '"IAmAliveTime":"[^"]*"')
                local updated = string.sub(row, 1, first - 1) .. '"IAmAliveTime":' .. ARGV[2] .. string.sub(row, last + 1)
                return redis.call('HSET', KEYS[1], ARGV[1], updated)
                """;
            var timestamp = JsonConvert.SerializeObject(entry.IAmAliveTime, _jsonSerializerSettings);
            await AwaitAsync(_db.ScriptEvaluateAsync(
                script,
                [_clusterKey],
                [entry.SiloAddress.ToString(), timestamp],
                CommandFlags.DemandMaster | CommandFlags.NoScriptCache), cancellationToken);
        }

        [Obsolete("Use UpdateRowAsync instead.")]
        public Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion) => UpdateRowAsync(entry, etag, tableVersion, CancellationToken.None);

        public async Task<bool> UpdateRowAsync(MembershipEntry entry, string etag, TableVersion tableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 读取操作使用表版本作为行 ETag，因此两者必须描述同一个视图。
            return string.Equals(etag, tableVersion.VersionEtag, StringComparison.Ordinal)
                && await UpsertRowInternal(entry, tableVersion, allowInsertOnly: false, cancellationToken);
        }

        [Obsolete("Use CleanupDefunctSiloEntriesAsync instead.")]
        public Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate) => CleanupDefunctSiloEntriesAsync(beforeDate, CancellationToken.None);

        public async Task CleanupDefunctSiloEntriesAsync(DateTimeOffset beforeDate, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = await AwaitAsync(_db.HashGetAllAsync(_clusterKey, CommandFlags.DemandMaster), cancellationToken);
            _ = GetTableVersionFromRow(rows.SingleOrDefault(row => row.Name == TableVersionKey).Value);
            foreach (var row in rows)
            {
                if (row.Name == TableVersionKey)
                {
                    continue;
                }

                var entry = Deserialize(row.Value.ToString());
                if (entry.Status == SiloStatus.Dead
                    && Math.Max(entry.IAmAliveTime.Ticks, entry.StartTime.Ticks) < beforeDate.UtcDateTime.Ticks
                    && entry.SuspectTimes?.Any(vote => vote.Item2 >= beforeDate.UtcDateTime) != true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    const string script =
                        """
                        if redis.call('HGET', KEYS[1], ARGV[1]) == ARGV[2] then
                            return redis.call('HDEL', KEYS[1], ARGV[1])
                        end
                        return 0
                        """;
                    await AwaitAsync(_db.ScriptEvaluateAsync(
                        script, [_clusterKey], [row.Name, row.Value], CommandFlags.DemandMaster | CommandFlags.NoScriptCache), cancellationToken);
                }
            }
        }

        // StackExchange.Redis 不接受取消令牌。如果调用方放弃等待，则观察最终的故障结果。
        private static async Task<T> AwaitAsync<T>(Task<T> operation, CancellationToken cancellationToken)
        {
            operation.Ignore();
            return await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            DetachOwnedMultiplexer()?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (DetachOwnedMultiplexer() is { } muxer)
            {
                await muxer.DisposeAsync().ConfigureAwait(false);
            }

            Task? deferredDisposal;
            lock (_lifecycleLock)
            {
                deferredDisposal = _deferredDisposal?.Task;
            }

            if (deferredDisposal is not null)
            {
                await deferredDisposal.ConfigureAwait(false);
            }
        }

        private IConnectionMultiplexer? DetachOwnedMultiplexer()
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                {
                    return null;
                }

                _disposed = true;
                if (_initializingCount == 0)
                {
                    _initializationLock.Dispose();
                }

                var ownedMuxer = _muxerIsShared ? null : _muxer;
                _muxer = null!;
                _db = null!;
                _muxerIsShared = false;
                IsInitialized = false;
                if (_initializingCount > 0 && ownedMuxer is not null)
                {
                    // 最后一个获准进入的初始化程序在完成后端操作后释放已发布的连接。
                    _deferredOwnedMultiplexer = ownedMuxer;
                    _deferredDisposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _deferredDisposal.Task.Ignore();
                    return null;
                }

                return ownedMuxer;
            }
        }

        private static string SerializeVersion(TableVersion tableVersion) => tableVersion.Version.ToString(CultureInfo.InvariantCulture);

        private static TableVersion DeserializeVersion(string versionString)
        {
            var version = int.Parse(versionString, CultureInfo.InvariantCulture);
            return new TableVersion(version, versionString);
        }

        private string Serialize(MembershipEntry value)
        {
            return JsonConvert.SerializeObject(value, _jsonSerializerSettings);
        }

        private MembershipEntry Deserialize(string json)
        {
            return JsonConvert.DeserializeObject<MembershipEntry>(json, _jsonSerializerSettings)!;
        }
    }
}
