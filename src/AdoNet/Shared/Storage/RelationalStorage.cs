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
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    /// <summary>
    /// 用于操作给定关系数据库及 ADO.NET 提供程序的通用类。
    /// </summary>
    [DebuggerDisplay("InvariantName = {InvariantName}, ConnectionString = {ConnectionString}")]
    internal class RelationalStorage : IRelationalStorage
    {
        /// <summary>
        /// 要使用的连接字符串。
        /// </summary>
        private readonly string _connectionString;

        /// <summary>
        /// 用于打开连接的可选数据源。
        /// </summary>
        private readonly DbDataSource? _dataSource;

        /// <summary>
        /// 此数据库连接器的固定名称（invariant name）。
        /// </summary>
        private readonly string _invariantName;

        /// <summary>
        /// 此存储的 ADO.NET 提供程序是否支持取消。此处会查询该功能
        /// 并缓存查询结果。
        /// </summary>
        private readonly bool _supportsCommandCancellation;

        /// <summary>
        /// 底层 ADO.NET 实现是否原生支持异步操作
        /// （即重写 ADO.NET Db*.XXXAsync 类中的异步实现）。
        /// </summary>
        private readonly bool _isSynchronousAdoNetImplementation;

        /// <summary>
        /// 给定数据提供程序的命令拦截器。
        /// </summary>
        private readonly ICommandInterceptor _databaseCommandInterceptor;

        /// <summary>
        /// 此数据库连接器的固定名称（invariant name）。
        /// </summary>
        public string InvariantName
        {
            get
            {
                return _invariantName;
            }
        }


        /// <summary>
        /// 用于连接数据库的连接字符串。
        /// </summary>
        public string ConnectionString
        {
            get
            {
                return _connectionString;
            }
        }


        /// <summary>
        /// 创建 <see cref="IRelationalStorage"/> 类型的数据库实例。
        /// </summary>
        /// <param name="invariantName">此数据库连接器的固定名称（invariant name）。</param>
        /// <param name="connectionString">此数据库执行数据库操作时应使用的连接字符串。</param>
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
        /// 创建 <see cref="IRelationalStorage"/> 类型的数据库实例。
        /// </summary>
        /// <param name="invariantName">此数据库连接器的固定名称（invariant name）。</param>
        /// <param name="dataSource">用于打开数据库连接的数据源。</param>
        /// <returns>关系存储实例。</returns>
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
        /// 使用恰好一个配置的连接来源创建实例。
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
        /// 执行给定语句，主要用于 <em>SELECT</em> 语句。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="query">执行给定语句，主要用于 <em>SELECT</em> 语句。</param>
        /// <param name="parameterProvider">向查询添加参数。参数名称必须与查询中的定义一致。</param>
        /// <param name="selector">此函数将原始 <see cref="IDataRecord"/> 结果转换为 <see paramref="TResult"/> 类型，其中 <see cref="int"/> 参数为结果集编号。</param>
        /// <param name="commandBehavior">应使用的命令行为。默认为 <see cref="CommandBehavior.Default"/>。</param>
        /// <param name="cancellationToken">取消令牌。默认为 <see cref="CancellationToken.None"/>。</param>
        /// <returns><see paramref="query"/> 的结果对象列表。</returns>
        /// <example>此示例演示如何执行手工优化的数据库调用。
        /// <code>
        /// //This struct holds the return value in this example.
        /// public struct Information
        /// {
        ///     public string TABLE_CATALOG { get; set; }
        ///     public string TABLE_NAME { get; set; }
        /// }
        ///
        /// //Here are defined two queries. There can be more than two queries, in which case
        /// //the result sets are differentiated by a count parameter. Here the queries are
        /// //SELECT clauses, but they can be whatever, even mixed ones.
        /// IEnumerable&lt;Information&gt; ret =
        ///     await storage.ReadAsync&lt;Information&gt;("SELECT * FROM INFORMATION_SCHEMA.TABLES; SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tp1", command =>
        /// {
        ///     //Parameters are added and created like this.
        ///     //They are database vendor agnostic.
        ///     var tp1 = command.CreateParameter();
        ///     tp1.ParameterName = "tp1";
        ///     tp1.Value = "some test value";
        ///     tp1.DbType = DbType.String;
        ///     tp1.Direction = ParameterDirection.Input;
        ///     command.Parameters.Add(tp1);
        ///
        ///     //The selector is used to select the results within the result set. In this case there are two homogenous
        ///     //result sets, so there is actually no need to check which result set the selector holds and it could
        ///     //marked with by convention by underscore (_).
        /// }, (selector, resultSetCount) =>
        ///    {
        ///        //This function is called once for each row returned, so the final result will be an
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
            // 如果查询是其他不可接受的内容（例如空字符串），则会抛出相应的数据库异常。
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
        /// 执行给定语句，主要用于 <em>INSERT</em>、<em>UPDATE</em>、<em>DELETE</em> 或 <em>DDL</em> 查询。
        /// </summary>
        /// <param name="query">要执行的查询。</param>
        /// <param name="parameterProvider">向查询添加参数。参数名称必须与查询中的定义一致。</param>
        /// <param name="commandBehavior">应使用的命令行为。默认为 <see cref="CommandBehavior.Default"/>。</param>
        /// <param name="cancellationToken">取消令牌。默认为 <see cref="CancellationToken.None"/>。</param>
        /// <returns>受影响的行数。</returns>
        /// <example>此示例演示如何执行手工优化的数据库调用。
        /// <code>
        /// //In contract to reading, execute queries are simpler as they return only
        /// //the affected rows count if it is available.
        /// var query = ""IF NOT EXISTS(SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Test') CREATE TABLE Test(Id INT PRIMARY KEY IDENTITY(1, 1) NOT NULL);"
        /// int affectedRowsCount = await storage.ExecuteAsync(query, command =>
        /// {
        ///     //There aren't parameters here, but they'd be added like when reading.
        ///     //As the affected rows count is the only thing returned, there isn't
        ///     //facilities to read anything.
        /// }).ConfigureAwait(continueOnCapturedContext: false);
        /// </code>
        /// </example>
        public async Task<int> ExecuteAsync(string query, Action<IDbCommand>? parameterProvider, CommandBehavior commandBehavior = CommandBehavior.Default, CancellationToken cancellationToken = default)
        {
            // 如果查询是其他不可接受的内容（例如空字符串），则会抛出相应的数据库异常。
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            return (await ExecuteAsync(query, parameterProvider, ExecuteReaderAsync, (unit, id, c) => Task.FromResult(unit), commandBehavior, cancellationToken).ConfigureAwait(false)).Item2;
        }

        /// <summary>
        /// 创建 <see cref="RelationalStorage"/> 类型的数据库实例。
        /// </summary>
        /// <param name="invariantName">此数据库连接器的固定名称（invariant name）。</param>
        /// <param name="connectionString">此数据库执行数据库操作时应使用的连接字符串。</param>
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
            // MSDN 文档指出，如果读取器已关闭，不应对 SqlCommand 调用 DbCommand.Cancel()，
            // 以避免竞态条件导致 SQL Server 在连接已关闭的情况下仍然流式传输
            // 结果集。来源：https://msdn.microsoft.com/en-us/library/system.data.sqlclient.sqlcommand.cancel(v=vs.110).aspx。
            // 在所有提供程序中强制执行此行为似乎也没有不利影响。
            var stateTuple = (Tuple<DbDataReader, DbCommand>)state!;
            if (!stateTuple.Item1.IsClosed)
            {
                stateTuple.Item2.Cancel();
            }
        }
    }
}

#nullable restore
