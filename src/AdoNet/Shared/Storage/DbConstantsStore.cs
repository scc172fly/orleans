using System.Collections.Generic;

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
namespace Orleans.Tests.SqlUtils
#else
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    internal static class DbConstantsStore
    {
        private static readonly Dictionary<string, DbConstants> invariantNameToConsts =
            new Dictionary<string, DbConstants>
            {
                {
                    AdoNetInvariants.InvariantNameSqlServer,
                    new DbConstants(startEscapeIndicator: '[',
                                    endEscapeIndicator: ']',
                                    unionAllSelectTemplate: " UNION ALL SELECT ",
                                    isSynchronousAdoNetImplementation: false,
                                    supportsStreamNatively: true,
                                    supportsCommandCancellation: true,
                                    commandInterceptor: NoOpCommandInterceptor.Instance)
                },
                {AdoNetInvariants.InvariantNameMySql, new DbConstants(
                                    startEscapeIndicator: '`',
                                    endEscapeIndicator: '`',
                                    unionAllSelectTemplate: " UNION ALL SELECT ",
                                    isSynchronousAdoNetImplementation: true,
                                    supportsStreamNatively: false,
                                    supportsCommandCancellation: false,
                                    commandInterceptor: NoOpCommandInterceptor.Instance)
                },
                {AdoNetInvariants.InvariantNamePostgreSql, new DbConstants(
                                    startEscapeIndicator: '"',
                                    endEscapeIndicator: '"',
                                    unionAllSelectTemplate: " UNION ALL SELECT ",
                                    isSynchronousAdoNetImplementation: false,
                                    supportsStreamNatively: true,
                                    supportsCommandCancellation: true, // 参阅 https://dev.mysql.com/doc/connector-net/en/connector-net-ref-mysqlclient-mysqlcommandmembers.html。
                                    commandInterceptor: NoOpCommandInterceptor.Instance)

                },
                {AdoNetInvariants.InvariantNameOracleDatabase, new DbConstants(
                                    startEscapeIndicator: '\"',
                                    endEscapeIndicator: '\"',
                                    unionAllSelectTemplate: " FROM DUAL UNION ALL SELECT ",
                                    isSynchronousAdoNetImplementation: true,
                                    supportsStreamNatively: false,
                                    supportsCommandCancellation: false, // 支持此功能，但文档中的备注令人担忧：https://docs.oracle.com/cd/E11882_01/win.112/e23174/OracleCommandClass.htm#DAFIEHHG。
                                    commandInterceptor: OracleCommandInterceptor.Instance)

                },
                {
                    AdoNetInvariants.InvariantNameMySqlConnector,
                    new DbConstants(startEscapeIndicator: '[',
                                    endEscapeIndicator: ']',
                                    unionAllSelectTemplate: " UNION ALL SELECT ",
                                    isSynchronousAdoNetImplementation: false,
                                    supportsStreamNatively: true,
                                    supportsCommandCancellation: true,
                                    commandInterceptor: NoOpCommandInterceptor.Instance)
                },
                {
                    AdoNetInvariants.InvariantNameSqlLite,
                    new DbConstants(startEscapeIndicator: '\"',
                                    endEscapeIndicator: '\"',
                                    unionAllSelectTemplate: " UNION ALL SELECT ",
                                    isSynchronousAdoNetImplementation: false,
                                    supportsStreamNatively: false,
                                    supportsCommandCancellation: true,
                                    commandInterceptor: NoOpCommandInterceptor.Instance)
            }
            };

        public static DbConstants GetDbConstants(string invariantName)
        {
            return invariantNameToConsts[invariantName];
        }

        /// <summary>
        /// 底层存储是否支持取消操作。
        /// </summary>
        /// <param name="storage">使用的存储。</param>
        /// <returns>如果支持取消，则为 <em>TRUE</em>；否则为 <em>FALSE</em>。</returns>
        public static bool SupportsCommandCancellation(this IRelationalStorage storage)
        {
            return SupportsCommandCancellation(storage.InvariantName);
        }


        /// <summary>
        /// 提供程序是否支持取消操作。
        /// </summary>
        /// <param name="adoNetProvider">ADO.NET 提供程序的固定名称字符串。</param>
        /// <returns>如果支持取消，则为 <em>TRUE</em>；否则为 <em>FALSE</em>。</returns>
        public static bool SupportsCommandCancellation(string adoNetProvider)
        {
            return GetDbConstants(adoNetProvider).SupportsCommandCancellation;
        }


        /// <summary>
        /// 底层存储是否原生支持流式传输。
        /// </summary>
        /// <param name="storage">使用的存储。</param>
        /// <returns>如果原生支持流式传输，则为 <em>TRUE</em>；否则为 <em>FALSE</em>。</returns>
        public static bool SupportsStreamNatively(this IRelationalStorage storage)
        {
            return SupportsStreamNatively(storage.InvariantName);
        }


        /// <summary>
        /// 提供程序是否原生支持流式传输。
        /// </summary>
        /// <param name="adoNetProvider">ADO.NET 提供程序的固定名称字符串。</param>
        /// <returns>如果原生支持流式传输，则为 <em>TRUE</em>；否则为 <em>FALSE</em>。</returns>
        public static bool SupportsStreamNatively(string adoNetProvider)
        {
            return GetDbConstants(adoNetProvider).SupportsStreamNatively;
        }


        /// <summary>
        /// 已知底层 ADO.NET 实现是否为同步实现。
        /// </summary>
        /// <param name="storage">使用的存储。</param>
        /// <returns></returns>
        public static bool IsSynchronousAdoNetImplementation(this IRelationalStorage storage)
        {
            // 目前假设除 MySQL 之外的所有实现均为异步实现。
            return IsSynchronousAdoNetImplementation(storage.InvariantName);
        }


        /// <summary>
        /// 提供程序是否支持取消操作。
        /// </summary>
        /// <param name="adoNetProvider">ADO.NET 提供程序的固定名称字符串。</param>
        /// <returns></returns>
        public static bool IsSynchronousAdoNetImplementation(string adoNetProvider)
        {
            return GetDbConstants(adoNetProvider).IsSynchronousAdoNetImplementation;
        }

        public static ICommandInterceptor GetDatabaseCommandInterceptor(string invariantName)
        {
            return GetDbConstants(invariantName).DatabaseCommandInterceptor;
        }
    }

    internal class DbConstants
    {
        /// <summary>
        /// UNION ALL SELECT 查询模板。
        /// </summary>
        public readonly string UnionAllSelectTemplate;

        /// <summary>
        /// 指示 ADO.NET 提供程序是否仅支持同步操作。
        /// </summary>
        public readonly bool IsSynchronousAdoNetImplementation;

        /// <summary>
        /// 指示 ADO.NET 提供程序是否原生支持流式操作。
        /// </summary>
        public readonly bool SupportsStreamNatively;

        /// <summary>
        /// 指示 ADO.NET 提供程序是否支持取消命令。
        /// </summary>
        public readonly bool SupportsCommandCancellation;

        /// <summary>
        /// 对名称为保留字的列和表进行转义时使用的起始转义字符。
        /// </summary>
        public readonly char StartEscapeIndicator;

        /// <summary>
        /// 对名称为保留字的列和表进行转义时使用的结束转义字符。
        /// </summary>
        public readonly char EndEscapeIndicator;

        public readonly ICommandInterceptor DatabaseCommandInterceptor;


        public DbConstants(char startEscapeIndicator, char endEscapeIndicator, string unionAllSelectTemplate,
                           bool isSynchronousAdoNetImplementation, bool supportsStreamNatively, bool supportsCommandCancellation, ICommandInterceptor commandInterceptor)
        {
            StartEscapeIndicator = startEscapeIndicator;
            EndEscapeIndicator = endEscapeIndicator;
            UnionAllSelectTemplate = unionAllSelectTemplate;
            IsSynchronousAdoNetImplementation = isSynchronousAdoNetImplementation;
            SupportsStreamNatively = supportsStreamNatively;
            SupportsCommandCancellation = supportsCommandCancellation;
            DatabaseCommandInterceptor = commandInterceptor;
        }
    }
}
