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
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    /// <summary>
    /// 支持所有系统存储的所有关系储存的分类:成员、提醒和统计
    /// </summary>
    internal class RelationalOrleansQueries
    {
        /// <summary>
        /// 基础存储
        /// </summary>
        private readonly IRelationalStorage storage;

        /// <summary>
        /// 当插入统计数据和生成批次插入条款时,这些是统计中的栏目
        /// 表格中将用多个值更新。其他的则仅用一个值更新。
        /// </summary>
        private static readonly string[] InsertStatisticsMultiupdateColumns = {
            DbStoredQueries.Columns.IsValueDelta,
            DbStoredQueries.Columns.StatValue,
            DbStoredQueries.Columns.Statistic
        };

        /// <summary>
        /// 排 orleans 职能查询
        /// </summary>
        private readonly DbStoredQueries dbStoredQueries;

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="storage">相关关联储存</param>
        /// <param name="dbStoredQueries">Orleans 功能查询</param>
        private RelationalOrleansQueries(IRelationalStorage storage, DbStoredQueries dbStoredQueries)
        {
            this.storage = storage;
            this.dbStoredQueries = dbStoredQueries;
        }

        /// <summary>
        /// 创建类型 <see cref="RelationalOrleansQueries"/> 的数据库实例,并初始化数据库中的查询 Orleans 。
        /// Orleans 仅使用这些查询及其中的变量,仅此而已。
        /// </summary>
        /// <param name="invariantName">此数据库连接器的变量名称。</param>
        /// <param name="connectionString">此数据库的连接字符串应用于数据库操作 。</param>
        internal static Task<RelationalOrleansQueries> CreateInstance(string invariantName, string connectionString) =>
            CreateInstance(RelationalStorage.CreateInstance(invariantName, connectionString));

        /// <summary>
        /// 使用一个已配置的连接源创建实例,并初始化数据库中的 Orleans 查询。
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
        /// 读取表格中的 Orleans 提醒数据。
        /// </summary>
        /// <param name="serviceId">服务编号。</param>
        /// <param name="grainId">下 grain 参考文献(ID)。</param>
        /// <returns>提醒表格数据。</returns>
        internal Task<ReminderTableData> ReadReminderRowsAsync(string serviceId, GrainId grainId)
        {
            // 收集查询仅产生含有提醒数据的行;选择器对单行外部连接是无效的。
            return ReadAsync(dbStoredQueries.ReadReminderRowsKey, record => GetReminderEntry(record)!, command =>
                new DbStoredQueries.Columns(command) { ServiceId = serviceId, GrainId = grainId.ToString() },
                ret => new ReminderTableData(ret.ToList()));
        }

        /// <summary>
        /// 读取表格中的 Orleans 提醒数据。
        /// </summary>
        /// <param name="serviceId">服务编号。</param>
        /// <param name="beginHash">开始散列。</param>
        /// <param name="endHash">末端散列。</param>
        /// <returns>提醒表格数据。</returns>
        internal Task<ReminderTableData> ReadReminderRowsAsync(string serviceId, uint beginHash, uint endHash)
        {
            var query = IsReminderRangeNonWrappingInSignedOrder(beginHash, endHash)
                ? dbStoredQueries.ReadRangeRows1Key
                : dbStoredQueries.ReadRangeRows2Key;

            // 收集查询仅产生含有提醒数据的行;选择器对单行外部连接是无效的。
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
            //具有非核字段, GrainId, 是指带有查询过滤选项的“ 查询筛选” 选项, 找到了一个条目 。
            string? grainId = record.GetValueOrDefault<string>(nameof(DbStoredQueries.Columns.GrainId));
            if (grainId != null)
            {
                return new ReminderEntry
                {
                    GrainId = GrainId.Parse(grainId),
                    ReminderName = record.GetValue<string>(nameof(DbStoredQueries.Columns.ReminderName)),
                    StartAt = record.GetDateTimeValue(nameof(DbStoredQueries.Columns.StartTime)),

                    //使用 GetInt64 方法,而不是通用 GetValue<TValue> 版本,从数据记录中检索值
                    //GetValue<int> 导致 InvalidCastException ,带有标准数据提供者。见 https://github.com/dotnet/orleans/issues/3561
                    Period = TimeSpan.FromMilliseconds(record.GetInt64(nameof(DbStoredQueries.Columns.Period))),
                    ETag = DbStoredQueries.Converters.GetVersion(record).ToString()
                };
            }
            return null;
        }
        /// <summary>
        /// 读取一行提醒数据。
        /// </summary>
        /// <param name="serviceId">身份证明处</param>
        /// <param name="grainId">下 grain 参考文献(ID)。</param>
        /// <param name="reminderName">要检索的提醒名称 。</param>
        /// <returns>其余的输入</returns>
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
        /// 插入或更新提醒行。
        /// </summary>
        /// <param name="serviceId">服务编号。</param>
        /// <param name="grainId">下 grain 参考文献(ID)。</param>
        /// <param name="reminderName">要检索的提醒名称 。</param>
        /// <param name="startTime">提醒的开始时间。</param>
        /// <param name="period">定期的催促,</param>
        /// <returns>或更新或插入提醒行的新标签。</returns>
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
        /// 删除提醒
        /// </summary>
        /// <param name="serviceId">身份证明处</param>
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
        /// 删除服务 ID 中的所有提醒行。
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
        /// 列出活动网关。 主要为 Orleans 客户端使用。
        /// </summary>
        /// <param name="deploymentId">用于查询网关的部署。</param>
        /// <param name="cancellationToken">取消查询所用的标记。</param>
        /// <returns> silo的网关。</returns>
        internal Task<List<Uri>> ActiveGatewaysAsync(string deploymentId, CancellationToken cancellationToken = default)
        {
            return ReadAsync(dbStoredQueries.GatewaysQueryKey, DbStoredQueries.Converters.GetGatewayUri, command =>
                new DbStoredQueries.Columns(command) { DeploymentId = deploymentId, Status = SiloStatus.Active },
                ret => ret.ToList(), cancellationToken);
        }

        /// <summary>
        /// 查询 Orleans 成员数据。
        /// </summary>
        /// <param name="deploymentId">用于查询数据的部署。</param>
        /// <param name="siloAddress">Silo 查询中用作参数的数据。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>成员表数据。</returns>
        internal Task<MembershipTableData> MembershipReadRowAsync(string deploymentId, SiloAddress siloAddress, CancellationToken cancellationToken = default)
        {
            return ReadAsync(dbStoredQueries.MembershipReadRowKey, DbStoredQueries.Converters.GetMembershipEntry, command =>
                new DbStoredQueries.Columns(command) { DeploymentId = deploymentId, SiloAddress = siloAddress },
                ConvertToMembershipTableData, cancellationToken);
        }

        /// <summary>
        /// 返回部署代号时的全部会籍数据
        /// </summary>
        /// <param name="deploymentId"></param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns></returns>
        internal Task<MembershipTableData> MembershipReadAllAsync(string deploymentId, CancellationToken cancellationToken = default)
        {
            return ReadAsync(dbStoredQueries.MembershipReadAllKey, DbStoredQueries.Converters.GetMembershipEntry, command =>
                new DbStoredQueries.Columns(command) { DeploymentId = deploymentId }, ConvertToMembershipTableData, cancellationToken);
        }

        /// <summary>
        /// 删除部署id 的所有成员条目
        /// </summary>
        /// <param name="deploymentId"></param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns></returns>
        internal Task DeleteMembershipTableEntriesAsync(string deploymentId, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(dbStoredQueries.DeleteMembershipTableEntriesKey, command =>
                new DbStoredQueries.Columns(command) { DeploymentId = deploymentId }, cancellationToken);
        }

        /// <summary>
        /// 运行已安装的清理查询, 当可选查询时使用抓取- row 资格 。
        /// </summary>
        /// <param name="beforeDate"></param>
        /// <param name="deploymentId"></param>
        /// <param name="cancellationToken">取消牌。</param>
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
                    // 评估 .NET 中的截止值,并与 SQL中的原始储存值匹配。
                    await ExecuteAsync(cleanupQuery, command =>
                    {
                        var columns = new DbStoredQueries.Columns(command)
                        {
                            DeploymentId = deploymentId,
                            SiloAddress = entry.SiloAddress,
                            SuspectTimes = entry.SuspectTimes
                        };
                        // 服务器SQL 的 DateTime 装订圆轮值,取自 DateTime2 栏。
                        var timestampType = storage.InvariantName == AdoNetInvariants.InvariantNameSqlServer ? DbType.DateTime2 : DbType.DateTime;
                        command.AddParameter(nameof(DbStoredQueries.Columns.StartTime), entry.StartTime, dbType: timestampType);
                        command.AddParameter(nameof(DbStoredQueries.Columns.IAmAliveTime), entry.IAmAliveTime, dbType: timestampType);
                        return columns;
                    }, cancellationToken);
                }
            }
        }

        /// <summary>
        /// 更新 IAmAlive a 的a silo
        /// </summary>
        /// <param name="deploymentId"></param>
        /// <param name="siloAddress"></param>
        /// <param name="iAmAliveTime"></param>
        /// <param name="cancellationToken">取消牌。</param>
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
        /// 如果版本行不存在,则插入该版本行。
        /// </summary>
        /// <param name="deploymentId">用于查询数据的部署。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns><em>真实</em> 如果插入了一行。 <em>平</em> 否则。</returns>
        internal Task<bool> InsertMembershipVersionRowAsync(string deploymentId, CancellationToken cancellationToken = default)
        {
            return ReadAsync(dbStoredQueries.InsertMembershipVersionKey, DbStoredQueries.Converters.GetSingleBooleanValue, command =>
                new DbStoredQueries.Columns(command) { DeploymentId = deploymentId }, ret => ret.First(), cancellationToken);
        }

        /// <summary>
        /// 如果还没有加入行,则插入该行。
        /// </summary>
        /// <param name="deploymentId">插入行的部署。</param>
        /// <param name="membershipEntry">插入成员入门数据。</param>
        /// <param name="etag">表格预期版本etag。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns><em>真实</em> 如果插入成功。 <em>平</em> 否则。</returns>
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
        /// <param name="deploymentId">插入行的部署。</param>
        /// <param name="membershipEntry">用于更新数据库的成员数据。</param>
        /// <param name="etag">表格预期版本etag。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns><em>真实</em> 如果更新了SUCCEEDS,则会更新。 <em>平</em> ot</returns>
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
        /// 将一条流信息排到流信息表格中。
        /// </summary>
        /// <param name="serviceId">服务标识符。</param>
        /// <param name="providerId">提供者标识符。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="payload">序列式事件有效载荷。</param>
        /// <param name="expiryTimeout">本事件批次的过期超时 。</param>
        /// <returns>承认信件被排队。</returns>
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
        /// 从流信息表格中获取流信息。
        /// </summary>
        /// <param name="serviceId">服务标识符。</param>
        /// <param name="providerId">提供者标识符。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="maxCount">要获取的事件批次的最大数 。</param>
        /// <param name="maxAttempts">锁定未处理事件批次的最大尝试。</param>
        /// <param name="visibilityTimeout">回收的事件批次的可见度超时。</param>
        /// <param name="removalTimeout">删除信件前的超时。</param>
        /// <param name="evictionInterval">机会性数据驱逐之间的间隔。</param>
        /// <param name="evictionBatchSize">每批驱逐信息的数量 。</param>
        /// <returns>一份已卸载的有效载荷清单。</returns>
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
        /// 确认从流信息表发送的信息。
        /// </summary>
        /// <param name="serviceId">服务标识符。</param>
        /// <param name="providerId">提供者标识符。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="messages">信件要确认。</param>
        /// <returns>确认书清单。</returns>
        /// <remarks>
        /// 如果 <paramref name="messages"/> 是空的, 那么返回一个空确认列表 。
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
        /// 使未经证实的流信息立即可供再传送。
        /// </summary>
        /// <param name="serviceId">服务标识符。</param>
        /// <param name="providerId">提供者标识符。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="messages">要发布的信件。</param>
        /// <returns>发布信息列表。</returns>
        /// <remarks>
        /// dequeue 反方作为收件,这样一个老化接收器不能发出电文
        /// 已经被一个新的接收器解密了
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

        // 以“1:2}3:4}5:6”的形式构建一个供应商中立的接收列表。
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
        /// 将发送失败逻辑应用到流信息,例如使电文再次可见或移动成死字。
        /// </summary>
        /// <param name="serviceId">服务标识符。</param>
        /// <param name="providerId">提供者标识符。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="messageId">信件标识符。</param>
        /// <param name="maxAttempts">在将信息移到死字之前,最大尝试发送次数。</param>
        /// <param name="removalTimeout">将信件保留在死信中的秒数。</param>
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
        /// 将符合条件的信息从流信息表格移至死信表格。
        /// </summary>
        /// <param name="serviceId">服务标识符。</param>
        /// <param name="providerId">提供者标识符。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="maxCount">批量中要移动的最大信件数量。</param>
        /// <param name="maxAttempts">信件可以解排的最大次数。</param>
        /// <param name="removalTimeout">删除信件前的超时。</param>
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
        /// 清除过期时间过期后从废信中删除信息 。
        /// </summary>
        /// <param name="serviceId">服务标识符。</param>
        /// <param name="providerId">提供者标识符。</param>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="maxCount">批量中要移动的最大信件数量。</param>
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
        /// 注册新的 grain 激活。
        /// </summary>
        /// <param name="clusterId">集束识别符号。</param>
        /// <param name="providerId">下 grain 目录提供者识别符 。</param>
        /// <param name="grainId"> grain 标识符。</param>
        /// <param name="siloAddress"> silo 地址。</param>
        /// <param name="activationId">激活标识符。</param>
        /// <returns>注册在目录中的 grain 激活。</returns>
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
        /// 解除登记启动 grain 。
        /// </summary>
        /// <param name="clusterId">集束识别符号。</param>
        /// <param name="providerId">下 grain 目录提供者识别符 。</param>
        /// <param name="grainId"> grain 标识符。</param>
        /// <param name="activationId">激活标识符。</param>
        /// <returns>受影响行的数。</returns>
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
        /// 检查一个 grain 激活。
        /// </summary>
        /// <param name="clusterId">集束识别符号。</param>
        /// <param name="providerId">下 grain 目录提供者识别符 。</param>
        /// <param name="grainId"> grain 标识符。</param>
        /// <returns>如果发现 grain 激活,则启动。</returns>
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
        /// 全部不登记者 grain 一组 silos.
        /// </summary>
        /// <param name="clusterId">集束识别符号。</param>
        /// <param name="providerId">下 grain 目录提供者识别符 。</param>
        /// <param name="siloAddresses">管断裂的 silos集。</param>
        /// <returns>受影响行的数。</returns>
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
