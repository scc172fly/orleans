using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Reflection;
using Orleans.Runtime;

#if CLUSTERING_ADONET
namespace Orleans.Clustering.AdoNet.Storage
#elif ORLEANS_REMINDERS_PROVIDER
using Orleans.Reminders.AdoNet.Converters;
namespace Orleans.Reminders.AdoNet.Storage
#elif PERSISTENCE_ADONET
namespace Orleans.Persistence.AdoNet.Storage
#elif REMINDERS_ADONET
namespace Orleans.Reminders.AdoNet.Storage
#elif STREAMING_ADONET
namespace Orleans.Streaming.AdoNet.Storage
#elif GRAINDIRECTORY_ADONET
namespace Orleans.GrainDirectory.AdoNet.Storage
#elif TESTER_SQLUTILS
namespace Orleans.Tests.SqlUtils
#else
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    /// <summary>
    /// 本类执行 Orleans 与相关关系储存之间的预期合同。
    /// 它确保所有储存的查询都在场,并且 
    /// </summary>
    internal class DbStoredQueries
    {
        private readonly Dictionary<string, string> queries;

        internal DbStoredQueries(Dictionary<string, string> queries)
        {
            var fields = typeof(DbStoredQueries).GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)
                .Select(p => p.Name);
            var missingQueryKeys = fields.Except(queries.Keys).ToArray();
            if (missingQueryKeys.Length > 0)
            {
                throw new ArgumentException(
                    $"Not all required queries found. Missing are: {string.Join(",", missingQueryKeys)}");
            }
            this.queries = queries;
        }

        /// <summary>
        /// 用来接收所有存储查询的查询
        /// 对所有关系带的分包来说,这很可能是一样的。
        /// </summary>
        internal const string GetQueriesKey = "SELECT QueryKey, QueryText FROM OrleansQuery";

#if CLUSTERING_ADONET || TESTER_SQLUTILS

        /// <summary>
        /// 获取网关 URI 的查询模板 。
        /// </summary>        
        internal string GatewaysQueryKey => queries[nameof(GatewaysQueryKey)];

        /// <summary>
        /// 检索一行会员数据的查询模板。
        /// </summary>        
        internal string MembershipReadRowKey => queries[nameof(MembershipReadRowKey)];

        /// <summary>
        /// 获取全部会籍数据的查询模板 。
        /// </summary>        
        internal string MembershipReadAllKey => queries[nameof(MembershipReadAllKey)];

        /// <summary>
        /// 插入会籍版本行的查询模板 。
        /// </summary>
        internal string InsertMembershipVersionKey => queries[nameof(InsertMembershipVersionKey)];

        /// <summary>
        /// 更新“ 我活着的时间”的查询模板。
        /// </summary>
        internal string UpdateIAmAlivetimeKey => queries[nameof(UpdateIAmAlivetimeKey)];

        /// <summary>
        /// 插入会籍行的查询模板。
        /// </summary>
        internal string InsertMembershipKey => queries[nameof(InsertMembershipKey)];

        /// <summary>
        /// 更新会籍行的查询模板 。
        /// </summary>
        internal string UpdateMembershipKey => queries[nameof(UpdateMembershipKey)];

        /// <summary>
        /// 用于删除会籍条目的查询模板 。
        /// </summary>
        internal string DeleteMembershipTableEntriesKey => queries[nameof(DeleteMembershipTableEntriesKey)];

        /// <summary>
        /// 清理已取消的查询模板 silo 条目 。
        /// </summary>
        internal string CleanupDefunctSiloEntriesKey => queries[nameof(CleanupDefunctSiloEntriesKey)];

        /// <summary>
        /// 获取删除一个其捕获活动值仍然匹配的死行的可选查询。
        /// </summary>
        internal string? GetCleanupDefunctSiloEntryQuery() => queries.GetValueOrDefault("CleanupDefunctSiloEntryKey");

#endif

#if REMINDERS_ADONET || TESTER_SQLUTILS || ORLEANS_REMINDERS_PROVIDER

        /// <summary>
        /// 读取提醒条目的查询模板 。
        /// </summary>
        internal string ReadReminderRowsKey => queries[nameof(ReadReminderRowsKey)];

        /// <summary>
        /// 用于阅读带范围提醒条目的查询模板。
        /// </summary>
        internal string ReadRangeRows1Key => queries[nameof(ReadRangeRows1Key)];

        /// <summary>
        /// 用于阅读带范围提醒条目的查询模板。
        /// </summary>
        internal string ReadRangeRows2Key => queries[nameof(ReadRangeRows2Key)];

        /// <summary>
        /// 用于阅读带范围提醒条目的查询模板。
        /// </summary>
        internal string ReadReminderRowKey => queries[nameof(ReadReminderRowKey)];

        /// <summary>
        /// 将提醒行上传到一个查询模板 。
        /// </summary>
        internal string UpsertReminderRowKey => queries[nameof(UpsertReminderRowKey)];

        /// <summary>
        /// 用于删除提醒行的查询模板。
        /// </summary>
        internal string DeleteReminderRowKey => queries[nameof(DeleteReminderRowKey)];

        /// <summary>
        /// 用于删除所有提醒行的查询模板。
        /// </summary>
        internal string DeleteReminderRowsKey => queries[nameof(DeleteReminderRowsKey)];

#endif

#if STREAMING_ADONET || TESTER_SQLUTILS

        /// <summary>
        /// 将信件输入流表的查询模板 。
        /// </summary>
        internal string QueueStreamMessageKey => queries[nameof(QueueStreamMessageKey)];

        /// <summary>
        /// 查询模板, 以从流表中删除信件 。
        /// </summary>
        internal string GetStreamMessagesKey => queries[nameof(GetStreamMessagesKey)];

        /// <summary>
        /// 用于确认从流表发送信息的信息的查询模板 。
        /// </summary>
        internal string ConfirmStreamMessagesKey => queries[nameof(ConfirmStreamMessagesKey)];

        /// <summary>
        /// 将单个信息移出查询模板( 将其移至死信) 。
        /// </summary>
        internal string FailStreamMessageKey => queries[nameof(FailStreamMessageKey)];

        /// <summary>
        /// 批量解密信件的查询模板( 将其移动到死信) 。
        /// </summary>
        internal string EvictStreamMessagesKey => queries[nameof(EvictStreamMessagesKey)];

        /// <summary>
        /// 将过期的已过期信件( 删除) 解密查询模板 。
        /// </summary>
        internal string EvictStreamDeadLettersKey => queries[nameof(EvictStreamDeadLettersKey)];

#endif

#if GRAINDIRECTORY_ADONET || TESTER_SQLUTILS

        /// <summary>
        /// 用于登记 grain 激活的查询模板。
        /// </summary>
        internal string RegisterGrainActivationKey => queries[nameof(RegisterGrainActivationKey)];

        /// <summary>
        /// 解登记 grain 激活的查询模板 。
        /// </summary>
        internal string UnregisterGrainActivationKey => queries[nameof(UnregisterGrainActivationKey)];

        /// <summary>
        /// 要查找 grain 激活的查询模板 。
        /// </summary>
        internal string LookupGrainActivationKey => queries[nameof(LookupGrainActivationKey)];

        /// <summary>
        /// 全部解登记查询模板 grain 一组 silos.
        /// </summary>
        internal string UnregisterGrainActivationsKey => queries[nameof(UnregisterGrainActivationsKey)];

#endif

        internal static class Converters
        {
            internal static KeyValuePair<string, string> GetQueryKeyAndValue(IDataRecord record)
            {
                return new KeyValuePair<string, string>(record.GetValue<string>("QueryKey"),
                    record.GetValue<string>("QueryText"));
            }


            internal static Tuple<MembershipEntry?, int> GetMembershipEntry(IDataRecord record)
            {
                //这是在目前版本中检查 是否有会员数据, 但如果有开始的时间, 有会员。            
                DateTime? startTime = record.GetDateTimeValueOrDefault(nameof(Columns.StartTime));
                MembershipEntry? entry = null;
                if (startTime.HasValue)
                {
                    // 遗留成员制可以省略 SiloName,但 MembershipEntry 保留兼容性的价值。
                    entry = new MembershipEntry
                    {
                        SiloAddress = GetSiloAddress(record, nameof(Columns.Port)),
                        SiloName = TryGetSiloName(record)!,
                        HostName = record.GetValue<string>(nameof(Columns.HostName)),
                        Status = (SiloStatus)Enum.Parse(typeof(SiloStatus), record.GetInt32(nameof(Columns.Status)).ToString()),
                        ProxyPort = record.GetInt32(nameof(Columns.ProxyPort)),
                        StartTime = startTime.Value,
                        IAmAliveTime = record.GetDateTimeValue(nameof(Columns.IAmAliveTime))
                    };

                    string? suspectingSilos = record.GetValueOrDefault<string>(nameof(Columns.SuspectTimes));
                    if (!string.IsNullOrWhiteSpace(suspectingSilos))
                    {
                        entry.SuspectTimes = new List<Tuple<SiloAddress, DateTime>>();
                        entry.SuspectTimes.AddRange(suspectingSilos.Split('|').Select(s =>
                        {
                            var split = s.Split(',');
                            return new Tuple<SiloAddress, DateTime>(SiloAddress.FromParsableString(split[0]),
                                LogFormatter.ParseDate(split[1]));
                        }));
                    }
                }

                return Tuple.Create(entry, GetVersion(record));
            }

            /// <summary>
            /// 这种方法是为了与成员表的兼容性,
            /// 字段中不包含 SiloName
            /// </summary>
            private static string? TryGetSiloName(IDataRecord record)
            {
                int pos;
                try
                {
                    pos = record.GetOrdinal(nameof(Columns.SiloName));
                }
                catch (IndexOutOfRangeException)
                {
                    return null;
                }

                return (string)record.GetValue(pos);

            }

            internal static int GetVersion(IDataRecord record)
            {
                return Convert.ToInt32(record.GetValue<object>(nameof(Version)));
            }

            internal static Uri GetGatewayUri(IDataRecord record)
            {
                return GetSiloAddress(record, nameof(Columns.ProxyPort)).ToGatewayUri();
            }

            private static SiloAddress GetSiloAddress(IDataRecord record, string portName)
            {
                //使用 GetInt32 方法,而不是通用 GetValue<TValue> 版本,从数据记录中检索值
                //GetValue<int> 导致有或卡数据提供者的 InvalidCastException 。见 https://github.com/dotnet/orleans/issues/3561
                int port = record.GetInt32(portName);
                int generation = record.GetInt32(nameof(Columns.Generation));
                string address = record.GetValue<string>(nameof(Columns.Address));
                var siloAddress = SiloAddress.New(IPAddress.Parse(address), port, generation);
                return siloAddress;
            }

            internal static bool GetSingleBooleanValue(IDataRecord record)
            {
                if (record.FieldCount != 1) throw new InvalidOperationException("Expected a single column");
                return Convert.ToBoolean(record.GetValue(0));
            }
        }

        internal class Columns
        {
            private readonly IDbCommand command;

            internal Columns(IDbCommand cmd)
            {
                command = cmd;

            }

            private void Add<T>(string paramName, T paramValue, DbType? dbType = null)
            {
                command.AddParameter(paramName, paramValue, dbType: dbType);
            }

            private void AddAddress(string name, IPAddress address)
            {
                Add(name, address.ToString(), dbType: DbType.AnsiString);
            }

            private void AddGrainHash(string name, uint grainHash)
            {
                Add(name, (int)grainHash);
            }

            internal string ClientId
            {
                set { Add(nameof(ClientId), value); }
            }

            internal int GatewayPort
            {
                set { Add(nameof(GatewayPort), value); }
            }

            internal IPAddress GatewayAddress
            {
                set { AddAddress(nameof(GatewayAddress), value); }
            }

            internal string SiloId
            {
                set { Add(nameof(SiloId), value); }
            }

            internal string Id
            {
                set { Add(nameof(Id), value); }
            }

            internal string Name
            {
                set { Add(nameof(Name), value); }
            }

            internal const string IsValueDelta = nameof(IsValueDelta);
            internal const string StatValue = nameof(StatValue);
            internal const string Statistic = nameof(Statistic);

            internal SiloAddress SiloAddress
            {
                set
                {
                    Address = value.Endpoint.Address;
                    Port = value.Endpoint.Port;
                    Generation = value.Generation;
                }
            }

            /// <summary>
            /// 工作周 SiloAddress 中在 Grain 目录与现有属性发生冲突 。
            /// </summary>
            internal string SiloAddressAsString
            {
                set => Add("SiloAddress", value);
            }

            internal string SiloAddresses
            {
                set => Add(nameof(SiloAddresses), value);
            }

            internal int Generation
            {
                set { Add(nameof(Generation), value); }
            }

            internal int Port
            {
                set { Add(nameof(Port), value); }
            }

            internal uint BeginHash
            {
                set { AddGrainHash(nameof(BeginHash), value); }
            }

            internal uint EndHash
            {
                set { AddGrainHash(nameof(EndHash), value); }
            }

            internal uint GrainHash
            {
                set { AddGrainHash(nameof(GrainHash), value); }
            }

            internal DateTime StartTime
            {
                set { Add(nameof(StartTime), value); }
            }

            internal IPAddress Address
            {
                set { AddAddress(nameof(Address), value); }
            }

            internal string ServiceId
            {
                set { Add(nameof(ServiceId), value); }
            }

            internal string ClusterId
            {
                set { Add(nameof(ClusterId), value); }
            }

            internal string DeploymentId
            {
                set { Add(nameof(DeploymentId), value); }
            }

            internal string SiloName
            {
                set { Add(nameof(SiloName), value); }
            }

            internal string HostName
            {
                set { Add(nameof(HostName), value); }
            }

            internal string Version
            {
                set { Add(nameof(Version), int.Parse(value)); }
            }

            internal DateTime IAmAliveTime
            {
                set { Add(nameof(IAmAliveTime), value); }
            }

            internal string GrainId
            {
                set { Add(nameof(GrainId), value, dbType: DbType.AnsiString); }
            }

            internal int GrainIdHash
            {
                set => Add(nameof(GrainIdHash), value);
            }

            internal string ReminderName
            {
                set { Add(nameof(ReminderName), value); }
            }

            internal TimeSpan Period
            {
                set
                {
                    if (value.TotalMilliseconds <= int.MaxValue)
                    {
                        // 使用旧的原制式时原始铸造。 这里用于保持后向兼容性
                        Add(nameof(Period), (int)value.TotalMilliseconds);
                    }
                    else
                    {
                        Add(nameof(Period), (long)value.TotalMilliseconds);
                    }
                }
            }

            internal SiloStatus Status
            {
                set { Add(nameof(Status), (int)value); }
            }

            internal int ProxyPort
            {
                set { Add(nameof(ProxyPort), value); }
            }

            internal List<Tuple<SiloAddress, DateTime>>? SuspectTimes
            {
                set
                {
                    Add(nameof(SuspectTimes), value == null
                        ? null
                        : string.Join("|", value.Select(
                            s => $"{s.Item1.ToParsableString()},{LogFormatter.PrintDate(s.Item2)}")));
                }
            }

            internal string QueueId
            {
                set => Add(nameof(QueueId), value);
            }

            internal long MessageId
            {
                set => Add(nameof(MessageId), value);
            }

            internal byte[] Payload
            {
                set => Add(nameof(Payload), value);
            }

            internal int ExpiryTimeout
            {
                set => Add(nameof(ExpiryTimeout), value);
            }

            internal int MaxCount
            {
                set => Add(nameof(MaxCount), value);
            }

            internal int MaxAttempts
            {
                set => Add(nameof(MaxAttempts), value);
            }

            internal int RemovalTimeout
            {
                set => Add(nameof(RemovalTimeout), value);
            }

            internal int VisibilityTimeout
            {
                set => Add(nameof(VisibilityTimeout), value);
            }

            internal int EvictionInterval
            {
                set => Add(nameof(EvictionInterval), value);
            }

            internal int EvictionBatchSize
            {
                set => Add(nameof(EvictionBatchSize), value);
            }

            internal string EventIds
            {
                set => Add(nameof(EventIds), value);
            }

            internal string ProviderId
            {
                set => Add(nameof(ProviderId), value);
            }

            internal string Items
            {
                set => Add(nameof(Items), value);
            }

            internal string ActivationId
            {
                set => Add(nameof(ActivationId), value);
            }
        }
    }
}

#nullable restore
