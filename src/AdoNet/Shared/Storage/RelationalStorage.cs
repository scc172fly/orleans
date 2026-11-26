using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    /// <summary>
    /// 与特定关系数据库和 ADO.NET 提供者合作的一般用途类。
    /// </summary>
    [DebuggerDisplay("InvariantName = {InvariantName}, ConnectionString = {ConnectionString}")]
    internal class RelationalStorage : IRelationalStorage
    {
        /// <summary>
        /// 要使用的连接字符串 。
        /// </summary>
        private readonly string _connectionString;

        /// <summary>
        /// 用于打开连接的可选数据源 。
        /// </summary>
        private readonly DbDataSource? _dataSource;

        /// <summary>
        /// 此数据库连接器的变量名称 。
        /// </summary>
        private readonly string _invariantName;

        /// <summary>
        /// 如果此存储的 ADO.NET 提供者支持不支持注销。
        /// 查询能力,结果在此缓存。
        /// </summary>
        private readonly bool _supportsCommandCancellation;

        /// <summary>
        /// 如果基本 ADO.NET 执行是本地非同步的
        /// (和 ADO.NET 调数b*. XXXAsync课程是否被推翻)或否。
        /// </summary>
        private readonly bool _isSynchronousAdoNetImplementation;

        /// <summary>
        /// 给定数据提供者的命令截击器。
        /// </summary>
        private readonly ICommandInterceptor _databaseCommandInterceptor;

        /// <summary>
        /// 此数据库连接器的变量名称 。
        /// </summary>
        public string InvariantName
        {
            get
            {
                return _invariantName;
            }
        }


        /// <summary>
        /// 连接到数据库的连接字符串。
        /// </summary>
        public string ConnectionString
        {
            get
            {
                return _connectionString;
            }
        }


        /// <summary>
        /// 创建类型 <see cref="IRelationalStorage"/>的数据库实例。
        /// </summary>
        /// <param name="invariantName">此数据库连接器的变量名称。</param>
        /// <param name="connectionString">此数据库的连接字符串应用于数据库操作 。</param>
        /// <returns></returns>
        public static IRelationalStorage CreateInstance(string invariantName, string connectionString)
        {
            if (string.IsNullOrWhiteSpace(invariantName))
            {
                throw new ArgumentException("The name of invariant must contain characters", nameof(invariantName));
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string must contain characters", nameof(connectionString));
            }

            return new RelationalStorage(invariantName, connectionString);
        }

        /// <summary>
        /// 创建类型 <see cref="IRelationalStorage"/>的数据库实例。
        /// </summary>
        /// <param name="invariantName">此数据库连接器的变量名称。</param>
        /// <param name="dataSource">用于打开数据库连接的数据源。</param>
        /// <returns>相关存储实例。</returns>
        public static IRelationalStorage CreateInstance(string invariantName, DbDataSource dataSource)
        {
            if (string.IsNullOrWhiteSpace(invariantName))
            {
                throw new ArgumentException("The name of invariant must contain characters", nameof(invariantName));
            }

            ArgumentNullException.ThrowIfNull(dataSource);
            DbConnectionFactory.ValidateDataSource(invariantName, dataSource);
            return new RelationalStorage(invariantName, dataSource);
        }

        /// <summary>
        /// 使用一个已配置的连接源创建实例。
        /// </summary>
        public static IRelationalStorage CreateInstance(string invariantName, string? connectionString, DbDataSource? dataSource)
        {
            if (string.IsNullOrWhiteSpace(connectionString) == (dataSource is null))
            {
                throw new ArgumentException($"Configure exactly one of {nameof(connectionString)} or {nameof(dataSource)}.");
            }

            return dataSource is null
                ? CreateInstance(invariantName, connectionString!)
                : CreateInstance(invariantName, dataSource);
        }


        /// <summary>
        /// 执行给定语句。 特别打算与 <em>SELECT</em> 语句一起使用。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="query">执行给定语句。 <em>选择</em> 语句。</param>
        /// <param name="parameterProvider">添加参数到查询中。参数名称必须与查询中所定义的名称匹配。</param>
        /// <param name="selector">此函数转换原始 <see cref="IDataRecord"/> 类型结果 <see paramref="TResult"/> 排 <see cref="int"/> 参数是结果图编号。</param>
        /// <param name="commandBehavior">应该使用的命令行为。 默认为 <see cref="CommandBehavior.Default"/>。</param>
        /// <param name="cancellationToken">取消牌。 默认为 <see cref="CancellationToken.None"/>。</param>
        /// <returns>由该天体组成的物体列表 <see paramref="query"/>.</returns>
        /// <example>这个样本显示如何使用手调数据库呼叫 。
        /// <code>
        /// // 此结构体保存本示例的返回值。
        /// public struct Information
        /// {
        ///     public string TABLE_CATALOG { get; set; }
        ///     public string TABLE_NAME { get; set; }
        /// }
        ///
        /// // 此处定义了两个查询。也可以定义更多查询，此时
        /// // 通过一个计数参数区分结果集。本例中的查询是
        /// // SELECT 语句，也可以使用或混用其他语句。
        /// IEnumerable&lt;Information&gt; ret =
        ///     await storage.ReadAsync&lt;Information&gt;("SELECT * FROM INFORMATION_SCHEMA.TABLES; SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tp1", command =>
        /// {
        ///     // 按此方式创建并添加参数。
        ///     // 这些参数与数据库供应商无关。
        ///     var tp1 = command.CreateParameter();
        ///     tp1.ParameterName = "tp1";
        ///     tp1.Value = "some test value";
        ///     tp1.DbType = DbType.String;
        ///     tp1.Direction = ParameterDirection.Input;
        ///     command.Parameters.Add(tp1);
        ///
        ///     // 选择器用于从结果集中选取结果。本例有两个结构相同的
        ///     // 结果集，因此无需判断选择器接收的是哪一个结果集，该参数可以
        ///     // 按惯例用下划线（_）标记。
        /// }, (selector, resultSetCount) =>
        ///    {
        ///        // 此函数对每个返回行调用一次，因此最终结果将是一个
        ///        //IEnumerable&lt;Information&gt;.
        ///        return new Information
        ///        {
        ///            TABLE_CATALOG = selector.GetValueOrDefault&lt;string&gt;("TABLE_CATALOG"),
        ///            TABLE_NAME = selector.GetValueOrDefault&lt;string&gt;("TABLE_NAME")
        ///        }
        ///}).ConfigureAwait(continueOnCapturedContext: false);
        /// </code>
        /// </example>
        public async Task<IEnumerable<TResult>> ReadAsync<TResult>(string query, Action<IDbCommand>? parameterProvider, Func<IDataRecord, int, CancellationToken, Task<TResult>> selector, CommandBehavior commandBehavior = CommandBehavior.Default, CancellationToken cancellationToken = default)
        {
            //如果查询是其它不可接受的(e.g. 空字符串),则将有一个适当的数据库例外。
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            return (await ExecuteAsync(query, parameterProvider, ExecuteReaderAsync, selector, commandBehavior, cancellationToken).ConfigureAwait(false)).Item1;
        }


        /// <summary>
        /// 执行给定的语句。 特别打算用于 <em>INSERT</em>、 <em>Uppate</em>、 <em>Delete</em> 或 <em>DDL</em> 查询。
        /// </summary>
        /// <param name="query">要执行的查询。</param>
        /// <param name="parameterProvider">添加参数到查询中。参数名称必须与查询中所定义的名称匹配。</param>
        /// <param name="commandBehavior">应该使用的命令行为。 默认为 <see cref="CommandBehavior.Default"/>。</param>
        /// <param name="cancellationToken">取消牌。 默认为 <see cref="CancellationToken.None"/>。</param>
        /// <returns>受影响的行数。</returns>
        /// <example>这个样本显示如何使用手调数据库呼叫 。
        /// <code>
        /// // 执行查询比读取查询更简单，它只返回
        /// // 受影响的行数（如果可用）。
        /// var query = ""IF NOT EXISTS(SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Test') CREATE TABLE Test(Id INT PRIMARY KEY IDENTITY(1, 1) NOT NULL);"
        /// int affectedRowsCount = await storage.ExecuteAsync(query, command =>
        /// {
        ///     // 此处没有参数。添加参数的方法与读取查询相同。
        ///     // 由于只返回受影响的行数，因此
        ///     // 无须读取其他数据。
        /// }).ConfigureAwait(continueOnCapturedContext: false);
        /// </code>
        /// </example>
        public async Task<int> ExecuteAsync(string query, Action<IDbCommand>? parameterProvider, CommandBehavior commandBehavior = CommandBehavior.Default, CancellationToken cancellationToken = default)
        {
            //如果查询是其它不可接受的(e.g. 空字符串),则将有一个适当的数据库例外。
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            return (await ExecuteAsync(query, parameterProvider, ExecuteReaderAsync, (unit, id, c) => Task.FromResult(unit), commandBehavior, cancellationToken).ConfigureAwait(false)).Item2;
        }

        /// <summary>
        /// 创建类型 <see cref="RelationalStorage"/>的数据库实例。
        /// </summary>
        /// <param name="invariantName">此数据库连接器的变量名称。</param>
        /// <param name="connectionString">此数据库的连接字符串应用于数据库操作 。</param>
        private RelationalStorage(string invariantName, string connectionString)
        {
            this._connectionString = connectionString;
            this._invariantName = invariantName;
            _supportsCommandCancellation = DbConstantsStore.SupportsCommandCancellation(InvariantName);
            _isSynchronousAdoNetImplementation = DbConstantsStore.IsSynchronousAdoNetImplementation(InvariantName);
            this._databaseCommandInterceptor = DbConstantsStore.GetDatabaseCommandInterceptor(InvariantName);
        }

        private RelationalStorage(string invariantName, DbDataSource dataSource)
        {
            _connectionString = dataSource.ConnectionString;
            _dataSource = dataSource;
            _invariantName = invariantName;
            _supportsCommandCancellation = DbConstantsStore.SupportsCommandCancellation(InvariantName);
            _isSynchronousAdoNetImplementation = DbConstantsStore.IsSynchronousAdoNetImplementation(InvariantName);
            _databaseCommandInterceptor = DbConstantsStore.GetDatabaseCommandInterceptor(InvariantName);
        }

        private static async Task<Tuple<IEnumerable<TResult>, int>> SelectAsync<TResult>(DbDataReader reader, Func<IDataReader, int, CancellationToken, Task<TResult>> selector, CancellationToken cancellationToken)
        {
            var results = new List<TResult>();
            var resultSetCount = 0;

            do
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
                {
                    var obj = await selector(reader, resultSetCount, cancellationToken).ConfigureAwait(false);
                    results.Add(obj);
                }

                ++resultSetCount;

            } while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));

            return Tuple.Create(results.AsEnumerable(), reader.RecordsAffected);
        }

        private async Task<Tuple<IEnumerable<TResult>, int>> ExecuteReaderAsync<TResult>(DbCommand command, Func<IDataRecord, int, CancellationToken, Task<TResult>> selector, CommandBehavior commandBehavior, CancellationToken cancellationToken)
        {
            using (var reader = await command.ExecuteReaderAsync(commandBehavior, cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
            {
                CancellationTokenRegistration cancellationRegistration = default;
                try
                {
                    if (cancellationToken.CanBeCanceled && _supportsCommandCancellation)
                    {
                        cancellationRegistration = cancellationToken.Register(CommandCancellation, Tuple.Create(reader, command), useSynchronizationContext: false);
                    }
                    return await SelectAsync(reader, selector, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                }
                finally
                {
                    await cancellationRegistration.DisposeAsync().ConfigureAwait(false);
                }
            }
        }


        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
            Justification = "Executes trusted SQL from provider query configuration; record and request values are bound separately by parameterProvider.")]
        private async Task<Tuple<IEnumerable<TResult>, int>> ExecuteAsync<TResult>(
            string query,
            Action<DbCommand>? parameterProvider,
            Func<DbCommand, Func<IDataRecord, int, CancellationToken, Task<TResult>>, CommandBehavior, CancellationToken, Task<Tuple<IEnumerable<TResult>, int>>> executor,
            Func<IDataRecord, int, CancellationToken, Task<TResult>> selector,
            CommandBehavior commandBehavior,
            CancellationToken cancellationToken)
        {
            using (var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
            {
                using (var command = connection.CreateCommand())
                {
                    parameterProvider?.Invoke(command);
                    command.CommandText = query;

                    _databaseCommandInterceptor.Intercept(command);

                    if (_isSynchronousAdoNetImplementation)
                    {
                        return await Task.Run(() => executor(command, selector, commandBehavior, cancellationToken), CancellationToken.None).ConfigureAwait(false);
                    }

                    return await executor(command, selector, commandBehavior, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        {
            if (_dataSource is not null)
            {
                return await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }

            var connection = DbConnectionFactory.CreateConnection(_invariantName, _connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }


        private static void CommandCancellation(object? state)
        {
            //MSDN文件显示,如果读者关闭了, DbCommand.Cancel()不应要求使用 SqlCommand
            //以避免出现导致 SQL 服务器流出结果集的种族状况
            //尽管连接已经关闭 。 资料来源: https://msdn.microsoft.com/en-us/library/system.data.sqlclient.sqlcommand.cancel(v=vs.110.aspx。
            //在所有提供者中实施这种行为似乎并不伤害所有人。
            var stateTuple = (Tuple<DbDataReader, DbCommand>)state!;
            if (!stateTuple.Item1.IsClosed)
            {
                stateTuple.Item2.Cancel();
            }
        }
    }
}

#nullable restore
