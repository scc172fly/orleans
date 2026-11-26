using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
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
    /// 方便功能对类型 <see cref="IRelationalStorage"/>的物体起作用。
    /// </summary>
    internal static class RelationalStorageExtensions
    {
        /// <summary>
        /// <see cref="IRelationalStorage.ReadAsync{TResult}"/>简化本
        /// </summary>
        /// <param name="storage"></param>
        /// <param name="query"></param>
        /// <param name="selector"></param>
        /// <param name="parameterProvider"></param>
        /// <typeparam name="TResult"></typeparam>
        /// <returns></returns>
        public static Task<IEnumerable<TResult>> ReadAsync<TResult>(this IRelationalStorage storage, string query, Func<IDataRecord, TResult> selector, Action<IDbCommand>? parameterProvider)
            => ReadAsync(storage, query, selector, parameterProvider, CancellationToken.None);

        /// <summary>
        /// 使用同步选择器读取查询结果,然后将取消的事宜转发给存储提供者。
        /// </summary>
        public static Task<IEnumerable<TResult>> ReadAsync<TResult>(this IRelationalStorage storage, string query, Func<IDataRecord, TResult> selector, Action<IDbCommand>? parameterProvider, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(selector);
            cancellationToken.ThrowIfCancellationRequested();
            return storage.ReadAsync(query, parameterProvider, (record, i, token) => Task.FromResult(selector(record)), cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 用途 <see cref="IRelationalStorage"/> 与 <see cref="DbExtensions.ReflectionParameterProvider{T}(IDbCommand, T, IReadOnlyDictionary{string, string})"/>.
        /// </summary>
        /// <typeparam name="TResult">结果的类型。</typeparam>
        /// <param name="storage">要使用的存储。</param>
        /// <param name="query">执行给定语句。 <em>选择</em> 声明,但与其他查询也一起工作。</param>
        /// <param name="parameters">添加参数到查询中。参数名称必须与查询中所定义的名称匹配。</param>
        /// <param name="cancellationToken">取消牌。 默认为 <see cref="CancellationToken.None"/>。</param>
        /// <returns>由该天体组成的物体列表 <see paramref="query"/>.</returns>
        /// <example>这使用反射来读取结果和匹配参数 。
        /// <code>
        /// // 此结构体保存本示例的返回值。
        /// public struct Information
        /// {
        ///     public string TABLE_CATALOG { get; set; }
        ///     public string TABLE_NAME { get; set; }
        /// }
        ///
        /// //Here reflection (<seealso cref="DbExtensions.ReflectionParameterProvider{T}(IDbCommand, T, IReadOnlyDictionary{string, string})"/>)
        /// 用于匹配参数名并读回结果（<seealso cref="DbExtensions.ReflectionSelector{TResult}(IDataRecord)"/>）。
        /// var query = "SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tname;";
        /// IEnumerable&lt;Information&gt; informationData = await db.ReadAsync&lt;Information&gt;(query, new { tname = 200000 });
        /// </code>
        /// </example>
        public static Task<IEnumerable<TResult>> ReadAsync<TResult>(this IRelationalStorage storage, string query, object? parameters, CancellationToken cancellationToken = default)
        {
            return storage.ReadAsync(query, command =>
            {
                if (parameters != null)
                {
                    command.ReflectionParameterProvider(parameters);
                }
            }, (selector, resultSetCount, token) => Task.FromResult(selector.ReflectionSelector<TResult>()), cancellationToken: cancellationToken);
        }


        /// <summary>
        /// 使用 <see cref="IRelationalStorage"/> 与 <see cref="DbExtensions.ReflectionParameterProvider{T}(System.Data.IDbCommand, T, IReadOnlyDictionary{string, string})">DbExtensions.ReflectionParameterProvider</see>。
        /// </summary>
        /// <typeparam name="TResult">结果的类型。</typeparam>
        /// <param name="storage">要使用的存储。</param>
        /// <param name="query">执行给定语句。 <em>选择</em> 声明,但与其他查询也一起工作。</param>
        /// <param name="cancellationToken">取消牌。 默认为 <see cref="CancellationToken.None"/>。</param>
        /// <returns>由该天体组成的物体列表 <see paramref="query"/>.</returns>
        public static Task<IEnumerable<TResult>> ReadAsync<TResult>(this IRelationalStorage storage, string query, CancellationToken cancellationToken = default)
        {
            return ReadAsync<TResult>(storage, query, null, cancellationToken);
        }


        /// <summary>
        /// 用途 <see cref="IRelationalStorage"/> 与 <see cref="DbExtensions.ReflectionSelector{TResult}(System.Data.IDataRecord)"/>.
        /// </summary>
        /// <param name="storage">要使用的存储。</param>
        /// <param name="query">执行给定的语句。 特别是用于 <em>Insert</em>、 <em>Uppdate</em>、 <em>Delete</em> 或 <em>DDL</em> 查询。</param>
        /// <param name="parameters">添加参数到查询中。参数名称必须与查询中所定义的名称匹配。</param>
        /// <param name="cancellationToken">取消牌。 默认为 <see cref="CancellationToken.None"/>。</param>
        /// <returns>受影响的行数。</returns>
        /// <example>此[使用反映为执行提供参数
        /// 查询中只读取有影响的行数(如果可用)。
        /// <code>
        /// //Here reflection (<seealso cref="DbExtensions.ReflectionParameterProvider{T}(IDbCommand, T, IReadOnlyDictionary{string, string})"/>)
        /// 用于匹配参数名并读回结果（<seealso cref="DbExtensions.ReflectionSelector{TResult}(IDataRecord)"/>）。
        /// var query = "IF NOT EXISTS(SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tname) CREATE TABLE Test(Id INT PRIMARY KEY IDENTITY(1, 1) NOT NULL);"
        /// await db.ExecuteAsync(query, new { tname = "test_table" });
        /// </code>
        /// </example>
        public static Task<int> ExecuteAsync(this IRelationalStorage storage, string query, object? parameters, CancellationToken cancellationToken = default)
        {
            return storage.ExecuteAsync(query, command =>
            {
                if (parameters != null)
                {
                    command.ReflectionParameterProvider(parameters);
                }
            }, cancellationToken: cancellationToken);
        }


        /// <summary>
        /// 用途 <see cref="IRelationalStorage"/> 与 <see cref="DbExtensions.ReflectionSelector{TResult}(System.Data.IDataRecord)"/>.
        /// </summary>
        /// <param name="storage">要使用的存储。</param>
        /// <param name="query">执行给定的语句。 特别是用于 <em>Insert</em>、 <em>Uppdate</em>、 <em>Delete</em> 或 <em>DDL</em> 查询。</param>
        /// <param name="cancellationToken">取消牌。 默认为 <see cref="CancellationToken.None"/>。</param>
        /// <returns>受影响的行数。</returns>
        public static Task<int> ExecuteAsync(this IRelationalStorage storage, string query, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(storage, query, null, cancellationToken);
        }


        /// <summary>
        /// 返回这些提供者的 <see cref="DbDataReader.GetStream(int)"/> 本地实施
        /// 。否则返回使用 <see cref="DbDataReader.GetBytes(int, long, byte[], int, int)"/>的块数读取。
        /// </summary>
        /// <param name="reader">返回流源的读者</param>
        /// <param name="ordinal">返回流的正则列。</param>
        /// <param name="storage">给贪婪者留下的存储器</param>
        /// <returns></returns>
        public static Stream GetStream(this DbDataReader reader, int ordinal, IRelationalStorage storage)
        {
            if (storage.SupportsStreamNatively())
            {
                return reader.GetStream(ordinal);
            }

            return new OrleansRelationalDownloadStream(reader, ordinal);
        }
    }
}

#nullable restore
