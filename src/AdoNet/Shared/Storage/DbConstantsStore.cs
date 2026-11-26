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
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
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
                                    supportsCommandCancellation: true, // 见 https://dev.mysql.com/doc/connector-net/en/connector-net-ref-mysqlclient-mysqlcommandmembers.html。
                                    commandInterceptor: NoOpCommandInterceptor.Instance)

                },
                {AdoNetInvariants.InvariantNameOracleDatabase, new DbConstants(
                                    startEscapeIndicator: '\"',
                                    endEscapeIndicator: '\"',
                                    unionAllSelectTemplate: " FROM DUAL UNION ALL SELECT ",
                                    isSynchronousAdoNetImplementation: true,
                                    supportsStreamNatively: false,
                                    supportsCommandCancellation: false, // 支持该词, 但这段话听起来很吓人: https://docs.oracle.com/cd/E11882_01/win.112/e23174/OracleCommandClass.htm#DAFIEHHG。
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
        /// 如果相关存储支持取消或不支持注销 。
        /// </summary>
        /// <param name="storage">使用的存储器。</param>
        /// <returns><em>真实</em> 如果支持取消, 则支持取消 。 <em>平</em> 否则。</returns>
        public static bool SupportsCommandCancellation(this IRelationalStorage storage)
        {
            return SupportsCommandCancellation(storage.InvariantName);
        }


        /// <summary>
        /// 如果提供商支持取消或不支持取消。
        /// </summary>
        /// <param name="adoNetProvider"> ADO.NET 提供方变量字符串。</param>
        /// <returns><em>真实</em> 如果支持取消, 则支持取消 。 <em>平</em> 否则。</returns>
        public static bool SupportsCommandCancellation(string adoNetProvider)
        {
            return GetDbConstants(adoNetProvider).SupportsCommandCancellation;
        }


        /// <summary>
        /// 如果基底存储支持本地流。
        /// </summary>
        /// <param name="storage">使用的存储器。</param>
        /// <returns><em>真实</em> 如果本地支持流 。 <em>平</em> 否则。</returns>
        public static bool SupportsStreamNatively(this IRelationalStorage storage)
        {
            return SupportsStreamNatively(storage.InvariantName);
        }


        /// <summary>
        /// 如果提供商支持本地流。
        /// </summary>
        /// <param name="adoNetProvider"> ADO.NET 提供方变量字符串。</param>
        /// <returns><em>真实</em> 如果本地支持流 。 <em>平</em> 否则。</returns>
        public static bool SupportsStreamNatively(string adoNetProvider)
        {
            return GetDbConstants(adoNetProvider).SupportsStreamNatively;
        }


        /// <summary>
        /// 如果基础 ADO.NET 执行已知同步。
        /// </summary>
        /// <param name="storage">使用的存储器。</param>
        /// <returns></returns>
        public static bool IsSynchronousAdoNetImplementation(this IRelationalStorage storage)
        {
            //目前,假设只是零零零 MySQL 之外的所有假设。
            return IsSynchronousAdoNetImplementation(storage.InvariantName);
        }


        /// <summary>
        /// 如果提供商支持取消或不支持取消。
        /// </summary>
        /// <param name="adoNetProvider"> ADO.NET 提供方变量字符串。</param>
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
        /// 全部选择的组合查询模板
        /// </summary>
        public readonly string UnionAllSelectTemplate;

        /// <summary>
        /// 指示 ADO.net 提供者是否只支持同步操作。
        /// </summary>
        public readonly bool IsSynchronousAdoNetImplementation;

        /// <summary>
        /// 指示 ADO.net 提供者是否按本地进行流业务。
        /// </summary>
        public readonly bool SupportsStreamNatively;

        /// <summary>
        /// 指示 ADO.net 提供者是否支持取消命令。
        /// </summary>
        public readonly bool SupportsCommandCancellation;

        /// <summary>
        /// 字符,该字符表示保留单词的列和表格的起始跳出键。
        /// </summary>
        public readonly char StartEscapeIndicator;

        /// <summary>
        /// 字符,该字符表示保留单词的列和表格的结尾跳出键。
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
