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
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    /// <summary>
    /// 用于操作 <see cref="IRelationalStorage"/> 类型对象的便捷函数。
    /// </summary>
    internal static class RelationalStorageExtensions
    {
        /// <summary>
        /// <see cref="IRelationalStorage.ReadAsync{TResult}"/> 的简化版本。
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
        /// 使用同步选择器读取查询结果，并将取消令牌传递给存储提供程序。
        /// </summary>
        public static Task<IEnumerable<TResult>> ReadAsync<TResult>(this IRelationalStorage storage, string query, Func<IDataRecord, TResult> selector, Action<IDbCommand>? parameterProvider, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(selector);
            cancellationToken.ThrowIfCancellationRequested();
            return storage.ReadAsync(query, parameterProvider, (record, i, token) => Task.FromResult(selector(record)), cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 将 <see cref="IRelationalStorage"/> 与 <see cref="DbExtensions.ReflectionParameterProvider{T}(IDbCommand, T, IReadOnlyDictionary{string, string})"/> 配合使用。
        /// </summary>
        /// <typeparam name="TResult">结果的类型。</typeparam>
        /// <param name="storage">要使用的存储。</param>
        /// <param name="query">执行给定语句，主要用于 <em>SELECT</em> 语句，但也适用于其他查询。</param>
        /// <param name="parameters">向查询添加参数。参数名称必须与查询中的定义一致。</param>
        /// <param name="cancellationToken">取消令牌。默认为 <see cref="CancellationToken.None"/>。</param>
        /// <returns><see paramref="query"/> 的结果对象列表。</returns>
        /// <example>此方法使用反射读取结果并匹配参数。
        /// <code>
        /// //This struct holds the return value in this example.
        /// public struct Information
        /// {
        ///     public string TABLE_CATALOG { get; set; }
        ///     public string TABLE_NAME { get; set; }
        /// }
        ///
        /// //Here reflection (<seealso cref="DbExtensions.ReflectionParameterProvider{T}(IDbCommand, T, IReadOnlyDictionary{string, string})"/>)
        /// is used to match parameter names as well as to read back the results (<seealso cref="DbExtensions.ReflectionSelector{TResult}(IDataRecord)"/>).
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
        /// 将 <see cref="IRelationalStorage"/> 与 <see cref="DbExtensions.ReflectionParameterProvider{T}(System.Data.IDbCommand, T, IReadOnlyDictionary{string, string})">DbExtensions.ReflectionParameterProvider</see> 配合使用。
        /// </summary>
        /// <typeparam name="TResult">结果的类型。</typeparam>
        /// <param name="storage">要使用的存储。</param>
        /// <param name="query">执行给定语句，主要用于 <em>SELECT</em> 语句，但也适用于其他查询。</param>
        /// <param name="cancellationToken">取消令牌。默认为 <see cref="CancellationToken.None"/>。</param>
        /// <returns><see paramref="query"/> 的结果对象列表。</returns>
        public static Task<IEnumerable<TResult>> ReadAsync<TResult>(this IRelationalStorage storage, string query, CancellationToken cancellationToken = default)
        {
            return ReadAsync<TResult>(storage, query, null, cancellationToken);
        }


        /// <summary>
        /// 将 <see cref="IRelationalStorage"/> 与 <see cref="DbExtensions.ReflectionSelector{TResult}(System.Data.IDataRecord)"/> 配合使用。
        /// </summary>
        /// <param name="storage">要使用的存储。</param>
        /// <param name="query">执行给定语句，主要用于 <em>INSERT</em>、<em>UPDATE</em>、<em>DELETE</em> 或 <em>DDL</em> 查询。</param>
        /// <param name="parameters">向查询添加参数。参数名称必须与查询中的定义一致。</param>
        /// <param name="cancellationToken">取消令牌。默认为 <see cref="CancellationToken.None"/>。</param>
        /// <returns>受影响的行数。</returns>
        /// <example>此方法使用反射为执行查询提供参数，
        /// 该查询仅在可获取受影响行数时读取该行数。
        /// <code>
        /// //Here reflection (<seealso cref="DbExtensions.ReflectionParameterProvider{T}(IDbCommand, T, IReadOnlyDictionary{string, string})"/>)
        /// is used to match parameter names as well as to read back the results (<seealso cref="DbExtensions.ReflectionSelector{TResult}(IDataRecord)"/>).
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
        /// 将 <see cref="IRelationalStorage"/> 与 <see cref="DbExtensions.ReflectionSelector{TResult}(System.Data.IDataRecord)"/> 配合使用。
        /// </summary>
        /// <param name="storage">要使用的存储。</param>
        /// <param name="query">执行给定语句，主要用于 <em>INSERT</em>、<em>UPDATE</em>、<em>DELETE</em> 或 <em>DDL</em> 查询。</param>
        /// <param name="cancellationToken">取消令牌。默认为 <see cref="CancellationToken.None"/>。</param>
        /// <returns>受影响的行数。</returns>
        public static Task<int> ExecuteAsync(this IRelationalStorage storage, string query, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(storage, query, null, cancellationToken);
        }


        /// <summary>
        /// 对于支持原生 <see cref="DbDataReader.GetStream(int)"/> 实现的提供程序，返回该实现；
        /// 否则使用 <see cref="DbDataReader.GetBytes(int, long, byte[], int, int)"/> 返回分块读取流。
        /// </summary>
        /// <param name="reader">用于返回流的读取器。</param>
        /// <param name="ordinal">列序号。</param>
        /// <param name="storage">提供固定名称（invariant name）的存储。</param>
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
