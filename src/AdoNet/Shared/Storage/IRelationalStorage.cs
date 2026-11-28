using System;
using System.Collections.Generic;
using System.Data;
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
    /// 所有关系数据库的公共接口。
    /// </summary>
    /// <remarks>
    /// 查询是由提供程序配置提供的受信任 SQL 语句。使用
    /// <c>parameterProvider</c> 通过命令参数提供记录值和请求值。
    /// </remarks>
#if TESTER_SQLUTILS
    public interface IRelationalStorage
#else
    internal interface IRelationalStorage
#endif
    {
        /// <summary>
        /// 执行给定语句，主要用于 <em>SELECT</em> 语句。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="query">要执行的查询。</param>
        /// <param name="parameterProvider">向查询添加参数。参数的顺序及名称必须与查询中的定义一致。</param>
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
        Task<IEnumerable<TResult>> ReadAsync<TResult>(string query, Action<IDbCommand>? parameterProvider, Func<IDataRecord, int, CancellationToken, Task<TResult>> selector, CommandBehavior commandBehavior = CommandBehavior.Default, CancellationToken cancellationToken = default);

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
        /// var query = ""IF NOT EXISTS(SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = N'Test') CREATE TABLE Test(Id INT PRIMARY KEY IDENTITY(1, 1) NOT NULL);"
        /// int affectedRowsCount = await storage.ExecuteAsync(query, command =>
        /// {
        ///     //There aren't parameters here, but they'd be added like when reading.
        ///     //As the affected rows count is the only thing returned, there isn't
        ///     //facilities to read anything.
        /// }).ConfigureAwait(continueOnCapturedContext: false);
        /// </code>
        /// </example>
        Task<int> ExecuteAsync(string query, Action<IDbCommand>? parameterProvider, CommandBehavior commandBehavior = CommandBehavior.Default, CancellationToken cancellationToken = default);

        /// <summary>
        /// 底层数据库的已知固定名称（invariant name）。
        /// </summary>
        string InvariantName { get; }

        /// <summary>
        /// 用于连接数据库的连接字符串。
        /// </summary>
        string ConnectionString { get; }
    }
}
