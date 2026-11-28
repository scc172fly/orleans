using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

#if CLUSTERING_ADONET
namespace Orleans.Clustering.AdoNet.Storage
#elif PERSISTENCE_ADONET
namespace Orleans.Persistence.AdoNet.Storage
#elif REMINDERS_ADONET
namespace Orleans.Reminders.AdoNet.Storage
#elif STREAMING_ADONET
namespace Orleans.Streaming.AdoNet.Storage
#elif GRAINDIRECTORY_ADONET
namespace Orleans.GrainDirectory.AdoNet.Storage
#elif TESTER_SQLUTILS
using Orleans.Streaming.AdoNet;
using Orleans.GrainDirectory.AdoNet;
namespace Orleans.Tests.SqlUtils
#else
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    /// <summary>
    /// 适用于支持所有系统存储（成员资格、提醒器及统计数据）的关系存储的类。
    /// </summary>
    internal class RelationalOrleansQueries
    {
        /// <summary>
        /// 底层存储。
        /// </summary>
        private readonly IRelationalStorage storage;

        /// <summary>
        /// 插入统计数据并生成批量插入语句时，统计表中的这些列
        /// 会使用多个值进行更新，其他列仅使用一个值更新。
        /// </summary>
        private static readonly string[] InsertStatisticsMultiupdateColumns = {
            DbStoredQueries.Columns.IsValueDelta,
            DbStoredQueries.Columns.StatValue,
            DbStoredQueries.Columns.Statistic
        };

        /// <summary>
        /// Orleans 功能查询。
        /// </summary>
        private readonly DbStoredQueries dbStoredQueries;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="storage">底层关系存储。</param>
        /// <param name="dbStoredQueries">Orleans 功能查询。</param>
        private RelationalOrleansQueries(IRelationalStorage storage, DbStoredQueries dbStoredQueries)
        {
            this.storage = storage;
            this.dbStoredQueries = dbStoredQueries;
        }

        /// <summary>
        /// 创建 <see cref="RelationalOrleansQueries"/> 类型的数据库实例，并从数据库初始化 Orleans 查询。
        /// Orleans 仅使用这些查询及其中的变量，不使用其他内容。
        /// </summary>
        /// <param name="invariantName">此数据库连接器的固定名称（invariant name）。</param>
        /// <param name="connectionString">此数据库执行数据库操作时应使用的连接字符串。</param>
        internal static Task<RelationalOrleansQueries> CreateInstance(string invariantName, string connectionString) =>
            CreateInstance(RelationalStorage.CreateInstance(invariantName, connectionString));

        /// <summary>
        /// 使用恰好一个配置的连接来源创建实例，并从数据库初始化 Orleans 查询。
        /// </summary>
        internal static Task<RelationalOrleansQueries> CreateInstance(string invariantName, string? connectionString, DbDataSource? dataSource, CancellationToken cancellationToken = default) =>
            CreateInstance(RelationalStorage.CreateInstance(invariantName, connectionString, dataSource), cancellationToken);

        internal static async Task<RelationalOrleansQueries> CreateInstance(IRelationalStorage storage, CancellationToken cancellationToken = default)
        {
            var queries = await storage.ReadAsync(DbStoredQueries.GetQueriesKey, DbStoredQueries.Converters.GetQueryKeyAndValue, null, cancellationToken);

            return new RelationalOrleansQueries(storage, new DbStoredQueries(queries.ToDictionary(q => q.Key, q => q.Value)));
        }

        private Task ExecuteAsync(string query, Func<IDbCommand, DbStoredQueries.Columns> parameterProvider, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return storage.ExecuteAsync(query, command => parameterProvider(command), cancellationToken: cancellationToken);
        }

        private async Task<TAggregate> ReadAsync<TResult, TAggregate>(string query,
            Func<IDataRecord, TResult> selector,
            Func<IDbCommand, DbStoredQueries.Columns> parameterProvider,
            Func<IEnumerable<TResult>, TAggregate> aggregator,
            CancellationToken cancellationToken = default)
        {
            var ret = await storage.ReadAsync(query, selector, command => parameterProvider(command), cancellationToken);
            return aggregator(ret);
        }

#if REMINDERS_ADONET || TESTER_SQLUTILS

        /// <summary>
        /// 从表中读取 Orleans 提醒器数据。
        /// </summary>
        /// <param name="serviceId">服务 ID。</param>
        /// <param name="grainId">Grain 引用（ID）。</param>
        /// <returns>提醒器表数据。</returns>
        internal Task<ReminderTableData> ReadReminderRowsAsync(string serviceId, GrainId grainId)
        {
            // 集合查询仅返回包含提醒器数据的行；对于单行外连接，选择器可以返回 null。
            return ReadAsync(dbStoredQueries.ReadReminderRowsKey, record => GetReminderEntry(record)!, command =>
                new DbStoredQueries.Columns(command) { ServiceId = serviceId, GrainId = grainId.ToString() },
                ret => new ReminderTableData(ret.ToList()));
        }

        /// <summary>
        /// 从表中读取 Orleans 提醒器数据。
        /// </summary>
        /// <param name="serviceId">服务 ID。</param>
        /// <param name="beginHash">起始哈希值。</param>
        /// <param name="endHash">结束哈希值。</param>
        /// <returns>提醒器表数据。</returns>
        internal Task<ReminderTableData> ReadReminderRowsAsync(string serviceId, uint beginHash, uint endHash)
        {
            var query = IsReminderRangeNonWrappingInSignedOrder(beginHash, endHash)
                ? dbStoredQueries.ReadRangeRows1Key
                : dbStoredQueries.ReadRangeRows2Key;

            // 集合查询仅返回包含提醒器数据的行；对于单行外连接，选择器可以返回 null。
            return ReadAsync<ReminderEntry, ReminderTableData>(query, record => GetReminderEntry(record)!, command =>
                new DbStoredQueries.Columns(command) { ServiceId = serviceId, BeginHash = beginHash, EndHash = endHash },
                ret => new ReminderTableData(ret.ToList()));
        }

        internal static bool IsReminderRangeNonWrappingInSignedOrder(uint beginHash, uint endHash)
            => unchecked((int)beginHash) < unchecked((int)endHash);

        internal static KeyValuePair<string, string> GetQueryKeyAndValue(IDataRecord record)
        {
            return new KeyValuePair<string, string>(record.GetValue<string>("QueryKey"),
                record.GetValue<string>("QueryText"));
        }

        internal static ReminderEntry? GetReminderEntry(IDataRecord record)
        {
            // GrainId 字段不为 null，意味着在当前查询筛选条件下找到了条目。
            string? grainId = record.GetValueOrDefault<string>(nameof(DbStoredQueries.Columns.GrainId));
            if (grainId != null)
            {
                return new ReminderEntry
                {
                    GrainId = GrainId.Parse(grainId),
                    ReminderName = record.GetValue<string>(nameof(DbStoredQueries.Columns.ReminderName)),
                    StartAt = record.GetDateTimeValue(nameof(DbStoredQueries.Columns.StartTime)),

                    // 使用 GetInt64 方法而不是泛型 GetValue<TValue> 版本从数据记录检索值。
                    // 使用 Oracle 数据提供程序时，GetValue<int> 会导致 InvalidCastException。参见 https://github.com/dotnet/orleans/issues/3561
                    Period = TimeSpan.FromMilliseconds(record.GetInt64(nameof(DbStoredQueries.Columns.Period))),
                    ETag = DbStoredQueries.Converters.GetVersion(record).ToString()
                };
            }
            return null;
        }
        /// <summary>
        /// 读取一行提醒器数据。
        /// </summary>
        /// <param name="serviceId">服务 ID。</param>
        /// <param name="grainId">Grain 引用（ID）。</param>
        /// <param name="reminderName">要检索的提醒器名称。</param>
        /// <returns>提醒器条目。</returns>
        internal Task<ReminderEntry?> ReadReminderRowAsync(string serviceId, GrainId grainId,
            string reminderName)
        {
            return ReadAsync(dbStoredQueries.ReadReminderRowKey, GetReminderEntry, command =>
                new DbStoredQueries.Columns(command)
                {
                    ServiceId = serviceId,
                    GrainId = grainId.ToString(),
                    ReminderName = reminderName
                }, ret => ret.FirstOrDefault());
        }

        /// <summary>
        /// 插入或更新提醒器行。
        /// </summary>
        /// <param name="serviceId">服务 ID。</param>
        /// <param name="grainId">Grain 引用（ID）。</param>
        /// <param name="reminderName">要检索的提醒器名称。</param>
        /// <param name="startTime">提醒器的开始时间。</param>
        /// <param name="period">提醒器的周期。</param>
        /// <returns>已更新或插入的提醒器行的新 ETag。</returns>
        internal Task<string?> UpsertReminderRowAsync(string serviceId, GrainId grainId,
            string reminderName, DateTime startTime, TimeSpan period)
        {
            return ReadAsync<int, string?>(dbStoredQueries.UpsertReminderRowKey, DbStoredQueries.Converters.GetVersion, command =>
                new DbStoredQueries.Columns(command)
                {
                    ServiceId = serviceId,
                    GrainHash = grainId.GetUniformHashCode(),
                    GrainId = grainId.ToString(),
                    ReminderName = reminderName,
                    StartTime = startTime,
                    Period = period
                }, ret => ret.First().ToString());
        }

        /// <summary>
        /// 删除提醒器。
        /// </summary>
        /// <param name="serviceId">服务 ID。</param>
        /// <param name="grainId"></param>
        /// <param name="reminderName"></param>
        /// <param name="etag"></param>
        /// <returns></returns>
        internal Task<bool> DeleteReminderRowAsync(string serviceId, GrainId grainId, string reminderName,
            string etag)
        {
            return ReadAsync(dbStoredQueries.DeleteReminderRowKey, DbStoredQueries.Converters.GetSingleBooleanValue, command =>
                new DbStoredQueries.Columns(command)
                {
                    ServiceId = serviceId,
                    GrainId = grainId.ToString(),
                    ReminderName = reminderName,
                    Version = etag
                }, ret => ret.First());
        }

        /// <summary>
        /// 删除指定服务 ID 的所有提醒器行。
        /// </summary>
        /// <param name="serviceId"></param>
        /// <returns></returns>
        internal Task DeleteReminderRowsAsync(string serviceId)
        {
            return ExecuteAsync(dbStoredQueries.DeleteReminderRowsKey, command =>
                new DbStoredQueries.Columns(command) { ServiceId = serviceId });
        }

#endif

#if CLUSTERING_ADONET || TESTER_SQLUTILS

        /// <summary>
        /// 列出活动网关，主要供 Orleans 客户端使用。
        /// </summary>
        /// <param name="deploymentId">要查询网关的部署。</param>
        /// <param name="cancellationToken">用于取消查询的取消令牌。</param>
        /// <returns>Silo 的网关。</returns>
        internal Task<List<Uri>> ActiveGatewaysAsync(string deploymentId, CancellationToken cancellationToken = default)
        {
            return ReadAsync(dbStoredQueries.GatewaysQueryKey, DbStoredQueries.Converters.GetGatewayUri, command =>
                new DbStoredQueries.Columns(command) { DeploymentId = deploymentId, Status = SiloStatus.Active },
                ret => ret.ToList(), cancellationToken);
        }

        /// <summary>
        /// 查询 Orleans 成员资格数据。
        /// </summary>
        /// <param name="deploymentId">要查询数据的部署。</param>
        /// <param name="siloAddress">用作查询参数的 Silo 数据。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>成员表数据。</returns>
        internal Task<MembershipTableData> MembershipReadRowAsync(string deploymentId, SiloAddress siloAddress, CancellationToken cancellationToken = default)
        {
            return ReadAsync(dbStoredQueries.MembershipReadRowKey, DbStoredQueries.Converters.GetMembershipEntry, command =>
                new DbStoredQueries.Columns(command) { DeploymentId = deploymentId, SiloAddress = siloAddress },
                ConvertToMembershipTableData, cancellationToken);
        }

        /// <summary>
        /// 返回指定部署 ID 的所有成员资格数据。
        /// </summary>
        /// <param name="deploymentId"></param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        internal Task<MembershipTableData> MembershipReadAllAsync(string deploymentId, CancellationToken cancellationToken = default)
        {
            return ReadAsync(dbStoredQueries.MembershipReadAllKey, DbStoredQueries.Converters.GetMembershipEntry, command =>
                new DbStoredQueries.Columns(command) { DeploymentId = deploymentId }, ConvertToMembershipTableData, cancellationToken);
        }

        /// <summary>
        /// 删除指定部署 ID 的所有成员条目。
        /// </summary>
        /// <param name="deploymentId"></param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        internal Task DeleteMembershipTableEntriesAsync(string deploymentId, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(dbStoredQueries.DeleteMembershipTableEntriesKey, command =>
                new DbStoredQueries.Columns(command) { DeploymentId = deploymentId }, cancellationToken);
        }

        /// <summary>
        /// 执行已安装的清理查询；如果可选查询可用，则根据捕获的行判断其是否符合清理条件。
        /// </summary>
        /// <param name="beforeDate"></param>
        /// <param name="deploymentId"></param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        internal async Task CleanupDefunctSiloEntriesAsync(DateTimeOffset beforeDate, string deploymentId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dbStoredQueries.GetCleanupDefunctSiloEntryQuery() is not { } cleanupQuery)
            {
                await ExecuteAsync(dbStoredQueries.CleanupDefunctSiloEntriesKey, command =>
                    new DbStoredQueries.Columns(command) { DeploymentId = deploymentId, IAmAliveTime = beforeDate.UtcDateTime }, cancellationToken);
                return;
            }

            var table = await MembershipReadAllAsync(deploymentId, cancellationToken);
            var cutoff = beforeDate.UtcDateTime;
            foreach (var (entry, _) in table.Members)
            {
                if (entry.Status == SiloStatus.Dead
                    && entry.StartTime < cutoff
                    && entry.IAmAliveTime < cutoff
                    && entry.SuspectTimes?.Any(vote => vote.Item2 >= cutoff) != true)
                {
                    // 在 .NET 中计算截止时间，并在 SQL 中匹配存储中的原始值。
                    await ExecuteAsync(cleanupQuery, command =>
                    {
                        var columns = new DbStoredQueries.Columns(command)
                        {
                            DeploymentId = deploymentId,
                            SiloAddress = entry.SiloAddress,
                            SuspectTimes = entry.SuspectTimes
                        };
                        // SQL Server 的 DateTime 绑定会舍入从 DateTime2 列读取的值。
                        var timestampType = storage.InvariantName == AdoNetInvariants.InvariantNameSqlServer ? DbType.DateTime2 : DbType.DateTime;
                        command.AddParameter(nameof(DbStoredQueries.Columns.StartTime), entry.StartTime, dbType: timestampType);
                        command.AddParameter(nameof(DbStoredQueries.Columns.IAmAliveTime), entry.IAmAliveTime, dbType: timestampType);
                        return columns;
                    }, cancellationToken);
                }
            }
        }

        /// <summary>
        /// 更新 Silo 的 IAmAlive。
        /// </summary>
        /// <param name="deploymentId"></param>
        /// <param name="siloAddress"></param>
        /// <param name="iAmAliveTime"></param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        internal Task UpdateIAmAliveTimeAsync(string deploymentId, SiloAddress siloAddress, DateTime iAmAliveTime, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(dbStoredQueries.UpdateIAmAlivetimeKey, command =>
                new DbStoredQueries.Columns(command)
                {
                    DeploymentId = deploymentId,
                    SiloAddress = siloAddress,
                    IAmAliveTime = iAmAliveTime
                }, cancellationToken);
        }

        /// <summary>
        /// 如果版本行尚不存在，则插入一行。
        /// </summary>
        /// <param name="deploymentId">要查询数据的部署。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>如果插入了行，则为 <em>TRUE</em>；否则为 <em>FALSE</em>。</returns>
        internal Task<bool> InsertMembershipVersionRowAsync(string deploymentId, CancellationToken cancellationToken = default)
        {
            return ReadAsync(dbStoredQueries.InsertMembershipVersionKey, DbStoredQueries.Converters.GetSingleBooleanValue, command =>
                new DbStoredQueries.Columns(command) { DeploymentId = deploymentId }, ret => ret.First(), cancellationToken);
        }

        /// <summary>
        /// 如果成员行尚不存在，则插入一行。
        /// </summary>
        /// <param name="deploymentId">要插入成员行的部署。</param>
        /// <param name="membershipEntry">要插入的成员条目数据。</param>
        /// <param name="etag">表的预期版本 ETag。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>如果插入成功，则为 <em>TRUE</em>；否则为 <em>FALSE</em>。</returns>
        internal Task<bool> InsertMembershipRowAsync(string deploymentId, MembershipEntry membershipEntry,
            string etag, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsMembershipVersionEtag(etag))
            {
                return Task.FromResult(false);
            }

            return ReadAsync(dbStoredQueries.InsertMembershipKey, DbStoredQueries.Converters.GetSingleBooleanValue, command =>
                new DbStoredQueries.Columns(command)
                {
                    DeploymentId = deploymentId,
                    IAmAliveTime = membershipEntry.IAmAliveTime,
                    SiloName = membershipEntry.SiloName,
                    HostName = membershipEntry.HostName,
                    SiloAddress = membershipEntry.SiloAddress,
                    StartTime = membershipEntry.StartTime,
                    Status = membershipEntry.Status,
                    ProxyPort = membershipEntry.ProxyPort,
                    Version = etag
                }, ret => ret.First(), cancellationToken);
        }

        /// <summary>
        /// 更新成员行数据。
        /// </summary>
        /// <param name="deploymentId">要插入成员行的部署。</param>
        /// <param name="membershipEntry">用于更新数据库的成员资格数据。</param>
        /// <param name="etag">表的预期版本 ETag。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>如果更新成功，则为 <em>TRUE</em>；否则为 <em>FALSE</em>。</returns>
        internal Task<bool> UpdateMembershipRowAsync(string deploymentId, MembershipEntry membershipEntry,
            string etag, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsMembershipVersionEtag(etag))
            {
                return Task.FromResult(false);
            }

            return ReadAsync(dbStoredQueries.UpdateMembershipKey, DbStoredQueries.Converters.GetSingleBooleanValue, command =>
                new DbStoredQueries.Columns(command)
                {
                    DeploymentId = deploymentId,
                    SiloAddress = membershipEntry.SiloAddress,
                    IAmAliveTime = membershipEntry.IAmAliveTime,
                    Status = membershipEntry.Status,
                    SuspectTimes = membershipEntry.SuspectTimes,
                    Version = etag
                }, ret => ret.First(), cancellationToken);
        }

        private static bool IsMembershipVersionEtag(string etag) =>
            int.TryParse(etag, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            && string.Equals(etag, version.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        private static MembershipTableData ConvertToMembershipTableData(IEnumerable<Tuple<MembershipEntry?, int>> ret)
        {
            var retList = ret.ToList();
            var tableVersionEtag = retList[0].Item2;
            var etag = tableVersionEtag.ToString(CultureInfo.InvariantCulture);
            var membershipEntries = new List<Tuple<MembershipEntry, string>>();
            if (retList[0].Item1 != null)
            {
                membershipEntries.AddRange(retList.Select(i => new Tuple<MembershipEntry, string>(i.Item1!, etag)));
            }
            return new MembershipTableData(membershipEntries, new TableVersion(tableVersionEtag, etag));
        }

#endif

#if STREAMING_ADONET || TESTER_SQLUTILS

        /// <summary>
        /// 将流消息加入流消息表中的队列。
        /// </summary>
        /// <param name="serviceId">服务标识符。</param>
        /// <param name="providerId">提供程序标识符。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="payload">序列化的事件有效载荷。</param>
        /// <param name="expiryTimeout">此事件批次的过期超时时长。</param>
        /// <returns>消息已加入队列的确认。</returns>
        internal Task<AdoNetStreamMessageAck> QueueStreamMessageAsync(string serviceId, string providerId, string queueId, byte[] payload, int expiryTimeout)
        {
            ArgumentNullException.ThrowIfNull(serviceId);
            ArgumentNullException.ThrowIfNull(providerId);
            ArgumentNullException.ThrowIfNull(queueId);

            return ReadAsync(
                dbStoredQueries.QueueStreamMessageKey,
                record => new AdoNetStreamMessageAck(
                    (string)record[nameof(AdoNetStreamMessageAck.ServiceId)],
                    (string)record[nameof(AdoNetStreamMessageAck.ProviderId)],
                    (string)record[nameof(AdoNetStreamMessageAck.QueueId)],
                    (long)record[nameof(AdoNetStreamMessageAck.MessageId)]),
                command => new DbStoredQueries.Columns(command)
                {
                    ServiceId = serviceId,
                    ProviderId = providerId,
                    QueueId = queueId,
                    Payload = payload,
                    ExpiryTimeout = expiryTimeout,
                },
                result => result.Single());
        }

        /// <summary>
        /// 从流消息表获取流消息。
        /// </summary>
        /// <param name="serviceId">服务标识符。</param>
        /// <param name="providerId">提供程序标识符。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="maxCount">要获取的最大事件批次数量。</param>
        /// <param name="maxAttempts">锁定尚未处理的事件批次的最大尝试次数。</param>
        /// <param name="visibilityTimeout">已检索事件批次的可见性超时时长。</param>
        /// <param name="removalTimeout">消息从死信中删除前的超时时长。</param>
        /// <param name="evictionInterval">机会式数据清理之间的时间间隔。</param>
        /// <param name="evictionBatchSize">每批清理的消息数量。</param>
        /// <returns>已出队的有效载荷列表。</returns>
        internal Task<IList<AdoNetStreamMessage>> GetStreamMessagesAsync(string serviceId, string providerId, string queueId, int maxCount, int maxAttempts, int visibilityTimeout, int removalTimeout, int evictionInterval, int evictionBatchSize)
        {
            ArgumentNullException.ThrowIfNull(serviceId);
            ArgumentNullException.ThrowIfNull(providerId);
            ArgumentNullException.ThrowIfNull(queueId);

            return ReadAsync<AdoNetStreamMessage, IList<AdoNetStreamMessage>>(
                dbStoredQueries.GetStreamMessagesKey,
                record => new AdoNetStreamMessage(
                    (string)record[nameof(AdoNetStreamMessage.ServiceId)],
                    (string)record[nameof(AdoNetStreamMessage.ProviderId)],
                    (string)record[nameof(AdoNetStreamMessage.QueueId)],
                    (long)record[nameof(AdoNetStreamMessage.MessageId)],
                    (int)record[nameof(AdoNetStreamMessage.Dequeued)],
                    (DateTime)record[nameof(AdoNetStreamMessage.VisibleOn)],
                    (DateTime)record[nameof(AdoNetStreamMessage.ExpiresOn)],
                    (DateTime)record[nameof(AdoNetStreamMessage.CreatedOn)],
                    (DateTime)record[nameof(AdoNetStreamMessage.ModifiedOn)],
                    (byte[])record[nameof(AdoNetStreamMessage.Payload)]),
                command => new DbStoredQueries.Columns(command)
                {
                    ServiceId = serviceId,
                    ProviderId = providerId,
                    QueueId = queueId,
                    MaxCount = maxCount,
                    MaxAttempts = maxAttempts,
                    VisibilityTimeout = visibilityTimeout,
                    RemovalTimeout = removalTimeout,
                    EvictionInterval = evictionInterval,
                    EvictionBatchSize = evictionBatchSize
                },
                result =>
                {
                    var messages = result.ToList();
                    messages.Sort(static (left, right) => left.MessageId.CompareTo(right.MessageId));
                    return messages;
                });
        }

        /// <summary>
        /// 确认来自流消息表的消息已投递。
        /// </summary>
        /// <param name="serviceId">服务标识符。</param>
        /// <param name="providerId">提供程序标识符。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="messages">要确认的消息。</param>
        /// <returns>确认列表。</returns>
        /// <remarks>
        /// 如果 <paramref name="messages"/> 为空，则返回空确认列表。
        /// </remarks>
        internal Task<IList<AdoNetStreamConfirmationAck>> ConfirmStreamMessagesAsync(string serviceId, string providerId, string queueId, IList<AdoNetStreamConfirmation> messages)
        {
            ArgumentNullException.ThrowIfNull(serviceId);
            ArgumentNullException.ThrowIfNull(providerId);
            ArgumentNullException.ThrowIfNull(queueId);
            ArgumentNullException.ThrowIfNull(messages);

            if (messages.Count == 0)
            {
                return Task.FromResult<IList<AdoNetStreamConfirmationAck>>([]);
            }

            return ReadAsync<AdoNetStreamConfirmationAck, IList<AdoNetStreamConfirmationAck>>(
                dbStoredQueries.ConfirmStreamMessagesKey,
                record => new AdoNetStreamConfirmationAck(
                    (string)record[nameof(AdoNetStreamConfirmationAck.ServiceId)],
                    (string)record[nameof(AdoNetStreamConfirmationAck.ProviderId)],
                    (string)record[nameof(AdoNetStreamConfirmationAck.QueueId)],
                    (long)record[nameof(AdoNetStreamConfirmationAck.MessageId)]),
                command => new DbStoredQueries.Columns(command)
                {
                    ServiceId = serviceId,
                    ProviderId = providerId,
                    QueueId = queueId,
                    Items = FormatStreamConfirmations(messages, release: false)
                },
                result => result.ToList());
        }

        /// <summary>
        /// 使未确认的流消息立即可重新投递。
        /// </summary>
        /// <param name="serviceId">服务标识符。</param>
        /// <param name="providerId">提供程序标识符。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="messages">要释放的消息。</param>
        /// <returns>已释放的消息列表。</returns>
        /// <remarks>
        /// 出队计数器充当回执，确保已过时的接收器无法释放
        /// 已由新接收器出队的消息。
        /// </remarks>
        internal Task<IList<AdoNetStreamConfirmationAck>> ReleaseStreamMessagesAsync(string serviceId, string providerId, string queueId, IList<AdoNetStreamConfirmation> messages)
        {
            ArgumentNullException.ThrowIfNull(serviceId);
            ArgumentNullException.ThrowIfNull(providerId);
            ArgumentNullException.ThrowIfNull(queueId);
            ArgumentNullException.ThrowIfNull(messages);

            if (messages.Count == 0)
            {
                return Task.FromResult<IList<AdoNetStreamConfirmationAck>>([]);
            }

            return ReadAsync<AdoNetStreamConfirmationAck, IList<AdoNetStreamConfirmationAck>>(
                dbStoredQueries.ConfirmStreamMessagesKey,
                record => new AdoNetStreamConfirmationAck(
                    (string)record[nameof(AdoNetStreamConfirmationAck.ServiceId)],
                    (string)record[nameof(AdoNetStreamConfirmationAck.ProviderId)],
                    (string)record[nameof(AdoNetStreamConfirmationAck.QueueId)],
                    (long)record[nameof(AdoNetStreamConfirmationAck.MessageId)]),
                command => new DbStoredQueries.Columns(command)
                {
                    ServiceId = serviceId,
                    ProviderId = providerId,
                    QueueId = queueId,
                    Items = FormatStreamConfirmations(messages, release: true)
                },
                result => result.ToList());
        }

        // 构造与提供程序无关、格式为“1:2|3:4|5:6”的回执列表。
        private static string FormatStreamConfirmations(IList<AdoNetStreamConfirmation> messages, bool release) =>
            messages.Aggregate(
                new StringBuilder(),
                (builder, message) => builder
                    .Append(builder.Length > 0 ? "|" : "")
                    .Append(message.MessageId)
                    .Append(':')
                    .Append(release ? -message.Dequeued : message.Dequeued),
                static builder => builder.ToString());

        /// <summary>
        /// 对流消息应用投递失败处理逻辑，例如使消息重新可见或将其移入死信。
        /// </summary>
        /// <param name="serviceId">服务标识符。</param>
        /// <param name="providerId">提供程序标识符。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="messageId">消息标识符。</param>
        /// <param name="maxAttempts">将消息移入死信前的最大投递尝试次数。</param>
        /// <param name="removalTimeout">消息在死信中保留的秒数。</param>
        internal Task FailStreamMessageAsync(string serviceId, string providerId, string queueId, long messageId, int maxAttempts, int removalTimeout)
        {
            ArgumentNullException.ThrowIfNull(serviceId);
            ArgumentNullException.ThrowIfNull(providerId);
            ArgumentNullException.ThrowIfNull(queueId);

            return ExecuteAsync(
                dbStoredQueries.FailStreamMessageKey,
                command => new DbStoredQueries.Columns(command)
                {
                    ServiceId = serviceId,
                    ProviderId = providerId,
                    QueueId = queueId,
                    MessageId = messageId,
                    MaxAttempts = maxAttempts,
                    RemovalTimeout = removalTimeout
                });
        }

        /// <summary>
        /// 将符合条件的消息从流消息表移入死信表。
        /// </summary>
        /// <param name="serviceId">服务标识符。</param>
        /// <param name="providerId">提供程序标识符。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="maxCount">此批次要移动的最大消息数量。</param>
        /// <param name="maxAttempts">消息可以出队的最大次数。</param>
        /// <param name="removalTimeout">消息从死信中删除前的超时时长。</param>
        internal Task EvictStreamMessagesAsync(string serviceId, string providerId, string queueId, int maxCount, int maxAttempts, int removalTimeout)
        {
            ArgumentNullException.ThrowIfNull(serviceId);
            ArgumentNullException.ThrowIfNull(providerId);
            ArgumentNullException.ThrowIfNull(queueId);

            return ExecuteAsync(
                dbStoredQueries.EvictStreamMessagesKey,
                command => new DbStoredQueries.Columns(command)
                {
                    ServiceId = serviceId,
                    ProviderId = providerId,
                    QueueId = queueId,
                    MaxCount = maxCount,
                    MaxAttempts = maxAttempts,
                    RemovalTimeout = removalTimeout
                });
        }

        /// <summary>
        /// 在死信消息的移除超时到期后将其移除。
        /// </summary>
        /// <param name="serviceId">服务标识符。</param>
        /// <param name="providerId">提供程序标识符。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="maxCount">此批次要移动的最大消息数量。</param>
        internal Task EvictStreamDeadLettersAsync(string serviceId, string providerId, string queueId, int maxCount)
        {
            ArgumentNullException.ThrowIfNull(serviceId);
            ArgumentNullException.ThrowIfNull(providerId);
            ArgumentNullException.ThrowIfNull(queueId);

            return ExecuteAsync(
                dbStoredQueries.EvictStreamDeadLettersKey,
                command => new DbStoredQueries.Columns(command)
                {
                    ServiceId = serviceId,
                    ProviderId = providerId,
                    QueueId = queueId,
                    MaxCount = maxCount
                });
        }

#endif

#if GRAINDIRECTORY_ADONET || TESTER_SQLUTILS

        /// <summary>
        /// 注册新的 Grain 激活实例。
        /// </summary>
        /// <param name="clusterId">集群标识符。</param>
        /// <param name="providerId">Grain 目录提供程序标识符。</param>
        /// <param name="grainId">Grain 标识符。</param>
        /// <param name="siloAddress">Silo 地址。</param>
        /// <param name="activationId">激活实例标识符。</param>
        /// <returns>在目录中注册的 Grain 激活实例。</returns>
        internal Task<AdoNetGrainDirectoryEntry> RegisterGrainActivationAsync(string clusterId, string providerId, string grainId, string siloAddress, string activationId)
        {
            ArgumentNullException.ThrowIfNull(clusterId);
            ArgumentNullException.ThrowIfNull(providerId);
            ArgumentNullException.ThrowIfNull(grainId);
            ArgumentNullException.ThrowIfNull(siloAddress);
            ArgumentNullException.ThrowIfNull(activationId);

            return ReadAsync(
                dbStoredQueries.RegisterGrainActivationKey,
                record => new AdoNetGrainDirectoryEntry(
                    (string)record[nameof(AdoNetGrainDirectoryEntry.ClusterId)],
                    (string)record[nameof(AdoNetGrainDirectoryEntry.ProviderId)],
                    (string)record[nameof(AdoNetGrainDirectoryEntry.GrainId)],
                    (string)record[nameof(AdoNetGrainDirectoryEntry.SiloAddress)],
                    (string)record[nameof(AdoNetGrainDirectoryEntry.ActivationId)]),
                command => new DbStoredQueries.Columns(command)
                {
                    ClusterId = clusterId,
                    ProviderId = providerId,
                    GrainId = grainId,
                    SiloAddressAsString = siloAddress,
                    ActivationId = activationId
                },
                result => result.Single());
        }

        /// <summary>
        /// 注销 Grain 激活实例。
        /// </summary>
        /// <param name="clusterId">集群标识符。</param>
        /// <param name="providerId">Grain 目录提供程序标识符。</param>
        /// <param name="grainId">Grain 标识符。</param>
        /// <param name="activationId">激活实例标识符。</param>
        /// <returns>受影响的行数。</returns>
        internal Task<int> UnregisterGrainActivationAsync(string clusterId, string providerId, string grainId, string activationId)
        {
            ArgumentNullException.ThrowIfNull(clusterId);
            ArgumentNullException.ThrowIfNull(providerId);
            ArgumentNullException.ThrowIfNull(grainId);
            ArgumentNullException.ThrowIfNull(activationId);

            return ReadAsync(
                dbStoredQueries.UnregisterGrainActivationKey,
                record => record.GetInt32(0),
                command => new DbStoredQueries.Columns(command)
                {
                    ClusterId = clusterId,
                    ProviderId = providerId,
                    GrainId = grainId,
                    ActivationId = activationId
                },
                result => result.Single());
        }

        /// <summary>
        /// 查找 Grain 激活实例。
        /// </summary>
        /// <param name="clusterId">集群标识符。</param>
        /// <param name="providerId">Grain 目录提供程序标识符。</param>
        /// <param name="grainId">Grain 标识符。</param>
        /// <returns>如果找到，则返回 Grain 激活实例；否则返回 null。</returns>
        internal Task<AdoNetGrainDirectoryEntry?> LookupGrainActivationAsync(string clusterId, string providerId, string grainId)
        {
            ArgumentNullException.ThrowIfNull(clusterId);
            ArgumentNullException.ThrowIfNull(providerId);
            ArgumentNullException.ThrowIfNull(grainId);

            return ReadAsync(
                dbStoredQueries.LookupGrainActivationKey,
                record => new AdoNetGrainDirectoryEntry(
                    (string)record[nameof(AdoNetGrainDirectoryEntry.ClusterId)],
                    (string)record[nameof(AdoNetGrainDirectoryEntry.ProviderId)],
                    (string)record[nameof(AdoNetGrainDirectoryEntry.GrainId)],
                    (string)record[nameof(AdoNetGrainDirectoryEntry.SiloAddress)],
                    (string)record[nameof(AdoNetGrainDirectoryEntry.ActivationId)]),
                command => new DbStoredQueries.Columns(command)
                {
                    ClusterId = clusterId,
                    ProviderId = providerId,
                    GrainId = grainId,
                },
                result => result.SingleOrDefault());
        }

        /// <summary>
        /// 注销一组 Silo 中的所有 Grain 激活实例。
        /// </summary>
        /// <param name="clusterId">集群标识符。</param>
        /// <param name="providerId">Grain 目录提供程序标识符。</param>
        /// <param name="siloAddresses">以竖线分隔的 Silo 集合。</param>
        /// <returns>受影响的行数。</returns>
        internal Task<int> UnregisterGrainActivationsAsync(string clusterId, string providerId, string siloAddresses)
        {
            ArgumentNullException.ThrowIfNull(clusterId);
            ArgumentNullException.ThrowIfNull(providerId);
            ArgumentNullException.ThrowIfNull(siloAddresses);

            return ReadAsync(
                dbStoredQueries.UnregisterGrainActivationsKey,
                record => record.GetInt32(0),
                command => new DbStoredQueries.Columns(command)
                {
                    ClusterId = clusterId,
                    ProviderId = providerId,
                    SiloAddresses = siloAddresses
                },
                result => result.Single());
        }

#endif
    }
}

#nullable restore
