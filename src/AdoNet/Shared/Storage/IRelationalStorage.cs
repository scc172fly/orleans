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
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    /// <summary>
    /// 所有关系数据库的共同界面。
    /// </summary>
    /// <remarks>
    /// 由供应商配置提供的查询报表可信 SQL 。
    /// 通过使用 <c>parameterProvider</c>命令参数。
    /// </remarks>
#if TESTER_SQLUTILS
    public interface IRelationalStorage
#else
    internal interface IRelationalStorage
#endif
    {
        /// <summary>
        /// 执行给定语句。 特别打算与 <em>SELECT</em> 语句一起使用。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="query">要执行的查询。</param>
        /// <param name="parameterProvider">添加参数到查询中。参数必须按相同顺序排列,名称与查询中所定义的名称相同。</param>
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
        Task<IEnumerable<TResult>> ReadAsync<TResult>(string query, Action<IDbCommand>? parameterProvider, Func<IDataRecord, int, CancellationToken, Task<TResult>> selector, CommandBehavior commandBehavior = CommandBehavior.Default, CancellationToken cancellationToken = default);

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
        /// var query = ""IF NOT EXISTS(SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = N'Test') CREATE TABLE Test(Id INT PRIMARY KEY IDENTITY(1, 1) NOT NULL);"
        /// int affectedRowsCount = await storage.ExecuteAsync(query, command =>
        /// {
        ///     // 此处没有参数。添加参数的方法与读取查询相同。
        ///     // 由于只返回受影响的行数，因此
        ///     // 无须读取其他数据。
        /// }).ConfigureAwait(continueOnCapturedContext: false);
        /// </code>
        /// </example>
        Task<int> ExecuteAsync(string query, Action<IDbCommand>? parameterProvider, CommandBehavior commandBehavior = CommandBehavior.Default, CancellationToken cancellationToken = default);

        /// <summary>
        /// 基础数据库的众所周知的不变名称。
        /// </summary>
        string InvariantName { get; }

        /// <summary>
        /// 连接到数据库的连接字符串。
        /// </summary>
        string ConnectionString { get; }
    }
}
