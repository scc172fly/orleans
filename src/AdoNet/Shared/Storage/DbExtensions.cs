using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Globalization;
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
    /// 包含一些便捷方法，可与 <see cref="IRelationalStorage">IRelationalStorage</see> 和 <see cref="RelationalStorage">GenericRelationalStorage</see> 配合使用。
    /// </summary>
    internal static class DbExtensions
    {
        /// <summary>
        /// CLR 类型与数据库类型转换的显式映射。
        /// </summary>
        private static readonly ReadOnlyDictionary<Type, DbType> typeMap = new ReadOnlyDictionary<Type, DbType>(new Dictionary<Type, DbType>
        {
            { typeof(object),   DbType.Object },
            { typeof(int),      DbType.Int32 },
            { typeof(int?),     DbType.Int32 },
            { typeof(uint),     DbType.UInt32 },
            { typeof(uint?),    DbType.UInt32 },
            { typeof(long),     DbType.Int64 },
            { typeof(long?),    DbType.Int64 },
            { typeof(ulong),    DbType.UInt64 },
            { typeof(ulong?),   DbType.UInt64 },
            { typeof(float),    DbType.Single },
            { typeof(float?),   DbType.Single },
            { typeof(double),   DbType.Double },
            { typeof(double?),  DbType.Double },
            { typeof(decimal),  DbType.Decimal },
            { typeof(decimal?), DbType.Decimal },
            { typeof(short),    DbType.Int16 },
            { typeof(short?),   DbType.Int16 },
            { typeof(ushort),   DbType.UInt16 },
            { typeof(ushort?),  DbType.UInt16 },
            { typeof(byte),     DbType.Byte },
            { typeof(byte?),    DbType.Byte },
            { typeof(sbyte),    DbType.SByte },
            { typeof(sbyte?),   DbType.SByte },
            { typeof(bool),     DbType.Boolean },
            { typeof(bool?),    DbType.Boolean },
            { typeof(string),   DbType.String },
            { typeof(char),     DbType.StringFixedLength },
            { typeof(char?),    DbType.StringFixedLength },
            { typeof(Guid),     DbType.Guid },
            { typeof(Guid?),    DbType.Guid },
            // 为实现跨数据库兼容性而使用 DateTime。底层数据库表的列类型可以是 DateTime 或 DateTime2。
            { typeof(DateTime),     DbType.DateTime },
            { typeof(DateTime?),    DbType.DateTime },
            { typeof(TimeSpan),     DbType.Time },
            { typeof(byte[]),       DbType.Binary },
            { typeof(TimeSpan?),        DbType.Time },
            { typeof(DateTimeOffset),   DbType.DateTimeOffset },
            { typeof(DateTimeOffset?),  DbType.DateTimeOffset },
        });

        /// <summary>
        /// 使用给定参数创建新的 SQL 参数。
        /// </summary>
        /// <typeparam name="T">参数类型。</typeparam>
        /// <param name="command">用于创建参数的命令。</param>
        /// <param name="direction">参数方向。</param>
        /// <param name="parameterName">参数名称。</param>
        /// <param name="value">参数值。</param>
        /// <param name="size">参数值的大小。</param>
        /// <param name="dbType">参数的 <see cref="DbType"/>。</param>
        /// <returns>使用给定参数创建的参数对象。</returns>
        public static IDbDataParameter CreateParameter<T>(this IDbCommand command, ParameterDirection direction, string parameterName, T? value, int? size = null, DbType? dbType = null)
        {
            // 值类型不应发生装箱。请参阅：
            //http://stackoverflow.com/questions/8823239/comparing-a-generic-against-null-that-could-be-a-value-or-reference-type
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.Value = (object?)value ?? DBNull.Value;
            parameter.DbType = dbType ?? typeMap[typeof(T)];
            parameter.Direction = direction;
            if (size != null) { parameter.Size = size.Value; }

            return parameter;
        }

        /// <summary>
        /// 创建新的 SQL 参数，并将其添加到命令中。
        /// </summary>
        /// <typeparam name="T">参数类型。</typeparam>
        /// <param name="command">用于创建参数的命令。</param>
        /// <param name="parameterName">参数名称。</param>
        /// <param name="value">参数值。</param>
        /// <param name="direction">参数方向。</param>
        /// <param name="size">参数值的大小。</param>
        /// <param name="dbType">参数的 <see cref="DbType"/>。</param>
        public static void AddParameter<T>(this IDbCommand command, string parameterName, T? value, ParameterDirection direction = ParameterDirection.Input, int? size = null, DbType? dbType = null)
        {
            command.Parameters.Add(command.CreateParameter(direction, parameterName, value, size, dbType));
        }

        /// <summary>
        /// 如果值不为 <see cref="System.DBNull"/>，则返回该值；否则返回 <em>default(TValue)</em>。
        /// </summary>
        /// <typeparam name="TValue">请求的值的类型。</typeparam>
        /// <param name="record">从中获取值的记录。</param>
        /// <param name="fieldName">要检索的字段名称。</param>
        /// <param name="default">该位置的值为 <see cref="System.DBNull"/> 时使用的默认值。</param>
        /// <returns>给定的值，或所请求类型的默认值。</returns>
        /// <remarks>如果给定的 <see paramref="fieldName"/> 不存在，则此函数抛出异常。</remarks>
        public static TValue? GetValueOrDefault<TValue>(this IDataRecord record, string fieldName, TValue? @default = default)
        {

            try
            {
                var ordinal = record.GetOrdinal(fieldName);
                return record.IsDBNull(ordinal) ? @default : (TValue)record.GetValue(ordinal);
            }
            catch (IndexOutOfRangeException e)
            {
                throw new DataException($"Field '{fieldName}' not found in data record.", e);
            }
        }
        /// <summary>
        /// 如果值不为 <see cref="System.DBNull"/>，则返回该值；否则返回 <em>default</em>。
        /// </summary>
        /// <param name="record">从中获取值的记录。</param>
        /// <param name="fieldName">要检索的字段名称。</param>
        /// <param name="default">该位置的值为 <see cref="System.DBNull"/> 时使用的默认值。</param>
        /// <returns>给定的值，或 <see cref="System.DateTime"/>? 的默认值。</returns>
        /// <exception cref="DataException"/>
        /// <remarks>当连接器推断出不符合预期的类型时，需要这样的显式函数。
        /// 例如，Npgsql.NodaTime 会使 Npgsql 返回 Noda 类型，导致 Orleans 无法
        /// 使用该值，因为它期望 .NET <see cref="System.DateTime"/>。如果给定的 <see paramref="fieldName"/> 不存在，则此函数抛出异常。</remarks>
        public static DateTime? GetDateTimeValueOrDefault(this IDataRecord record, string fieldName, DateTime? @default = default)
        {
            try
            {
                var ordinal = record.GetOrdinal(fieldName);
                return record.IsDBNull(ordinal) ? @default : DateTime.SpecifyKind(record.GetDateTime(ordinal), DateTimeKind.Utc);
            }
            catch (IndexOutOfRangeException e)
            {
                throw new DataException($"Field '{fieldName}' not found in data record.", e);
            }
        }

        /// <summary>
        /// 如果值不为 <see cref="System.DBNull"/>，则返回该值；否则返回 <em>default(TValue)</em>。
        /// </summary>
        /// <typeparam name="TValue">请求的值的类型。</typeparam>
        /// <param name="record">从中获取值的记录。</param>
        /// <param name="fieldName">要检索的字段名称。</param>
        /// <param name="default">该位置的值为 <see cref="System.DBNull"/> 时使用的默认值。</param>
        /// <returns>给定的值，或所请求类型的默认值。</returns>
        /// <exception cref="DataException"/>
        /// <remarks>如果给定的 <see paramref="fieldName"/> 不存在，则此函数抛出异常。</remarks>
        public static async Task<TValue?> GetValueOrDefaultAsync<TValue>(this DbDataReader record, string fieldName, TValue? @default = default)
        {
            try
            {
                var ordinal = record.GetOrdinal(fieldName);
                return (await record.IsDBNullAsync(ordinal).ConfigureAwait(false))
                    ? @default
                    : (await record.GetFieldValueAsync<TValue>(ordinal).ConfigureAwait(false));
            }
            catch (IndexOutOfRangeException e)
            {
                throw new DataException($"Field '{fieldName}' not found in data record.", e);
            }
        }


        /// <summary>
        /// 如果值不为 <see cref="System.DBNull"/>，则返回该值；否则返回 <em>default(TValue)</em>。
        /// </summary>
        /// <typeparam name="TValue">请求的值的类型。</typeparam>
        /// <param name="record">从中获取值的记录。</param>
        /// <param name="ordinal">字段名称对应的序号。</param>
        /// <param name="default">该位置的值为 <see cref="System.DBNull"/> 时使用的默认值。</param>
        /// <returns>给定的值，或所请求类型的默认值。</returns>
        /// <exception cref="IndexOutOfRangeException"/>                
        public static TValue? GetValueOrDefault<TValue>(this IDataRecord record, int ordinal, TValue? @default = default)
        {
            return record.IsDBNull(ordinal) ? @default : (TValue)record.GetValue(ordinal);
        }


        /// <summary>
        /// 如果值不为 <see cref="System.DBNull"/>，则返回该值；否则返回 <em>default(TValue)</em>。
        /// </summary>
        /// <typeparam name="TValue">请求的值的类型。</typeparam>
        /// <param name="record">从中获取值的记录。</param>
        /// <param name="ordinal">字段名称对应的序号。</param>
        /// <param name="default">该位置的值为 <see cref="System.DBNull"/> 时使用的默认值。</param>
        /// <returns>给定的值，或所请求类型的默认值。</returns>
        /// <exception cref="IndexOutOfRangeException"/>                
        public static async Task<TValue?> GetValueOrDefaultAsync<TValue>(this DbDataReader record, int ordinal, TValue? @default = default)
        {

            return (await record.IsDBNullAsync(ordinal).ConfigureAwait(false)) ? @default : (await record.GetFieldValueAsync<TValue>(ordinal).ConfigureAwait(false));
        }


        /// <summary>
        /// 返回指定 <see paramref="fieldName"/> 对应的值。
        /// </summary>
        /// <typeparam name="TValue">要检索的值的类型。</typeparam>
        /// <param name="record">从中获取值的记录。</param>
        /// <param name="fieldName">字段名称。</param>
        /// <returns>由 <see paramref="fieldName"/> 指定的字段中的值。</returns>
        /// <exception cref="DataException"/>
        /// <remarks>如果给定的 <see paramref="fieldName"/> 不存在，则此函数抛出异常。</remarks>
        public static TValue GetValue<TValue>(this IDataRecord record, string fieldName)
        {
            try
            {
                var ordinal = record.GetOrdinal(fieldName);
                return (TValue)record.GetValue(ordinal);
            }
            catch (IndexOutOfRangeException e)
            {
                throw new DataException($"Field '{fieldName}' not found in data record.", e);
            }
        }
        /// <summary>
        /// 返回指定 <see cref="System.DateTime"/> 类型的值，其字段由 <see paramref="fieldName"/> 指定。
        /// </summary>
        /// <param name="record">从中获取值的记录。</param>
        /// <param name="fieldName">字段名称。</param>
        /// <returns>给定字段中的 DateTime 值。</returns>
        /// <exception cref="DataException"/>
        /// <remarks>当连接器推断出不符合预期的类型时，需要这样的显式函数。
        /// 例如，Npgsql.NodaTime 会使 Npgsql 返回 Noda 类型，导致 Orleans 无法
        /// 使用该值，因为它期望 .NET <see cref="System.DateTime"/>。如果给定的 <see paramref="fieldName"/> 不存在，则此函数抛出异常。</remarks>
        public static DateTime GetDateTimeValue(this IDataRecord record, string fieldName)
        {
            try
            {
                var ordinal = record.GetOrdinal(fieldName);
                return DateTime.SpecifyKind(record.GetDateTime(ordinal), DateTimeKind.Utc);
            }
            catch (IndexOutOfRangeException e)
            {
                throw new DataException($"Field '{fieldName}' not found in data record.", e);
            }
        }

        /// <summary>
        /// 使用固定区域性（invariant culture）将指定 <see paramref="fieldName"/> 对应的值转换为 int。
        /// </summary>
        /// <param name="record">从中获取值的记录。</param>
        /// <param name="fieldName">字段名称。</param>
        /// <exception cref="DataException"/>
        /// <returns>由 <see paramref="fieldName"/> 指定的字段中的整数值。</returns>
        public static int GetInt32(this IDataRecord record, string fieldName)
        {
            try
            {
                var ordinal = record.GetOrdinal(fieldName);
                return Convert.ToInt32(record.GetValue(ordinal), CultureInfo.InvariantCulture);
            }
            catch (IndexOutOfRangeException e)
            {
                throw new DataException($"Field '{fieldName}' not found in data record.", e);
            }
        }

        /// <summary>
        /// 将指定 <see paramref="fieldName"/> 对应的值作为 long 返回。
        /// </summary>
        /// <param name="record">从中获取值的记录。</param>
        /// <param name="fieldName">字段名称。</param>
        /// <exception cref="DataException"/>
        /// <returns>由 <see paramref="fieldName"/> 指定的字段中的整数值。</returns>
        public static long GetInt64(this IDataRecord record, string fieldName)
        {
            try
            {
                var ordinal = record.GetOrdinal(fieldName);
                // 使用旧版架构时的原始强制类型转换。为保持向后兼容性而保留。
                return record.GetFieldType(ordinal) == typeof(int) ? record.GetInt32(ordinal) : record.GetInt64(ordinal);
            }
            catch (IndexOutOfRangeException e)
            {
                throw new DataException($"Field '{fieldName}' not found in data record.", e);
            }
        }

        /// <summary>
        /// 使用固定区域性（invariant culture）将指定 <see paramref="fieldName"/> 对应的值转换为可空 int，
        /// 对于 <see cref="DBNull.Value"/> 返回 null。
        /// </summary>
        /// <param name="record">从中获取值的记录。</param>
        /// <param name="fieldName">字段名称。</param>
        /// <exception cref="DataException"/>
        /// <returns>由 <see paramref="fieldName"/> 指定的字段中的可空 int 值。</returns>
        public static int? GetNullableInt32(this IDataRecord record, string fieldName)
        {
            try
            {
                var ordinal = record.GetOrdinal(fieldName);
                var value = record.GetValue(ordinal);
                if (value == DBNull.Value)
                    return null;

                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (IndexOutOfRangeException e)
            {
                throw new DataException($"Field '{fieldName}' not found in data record.", e);
            }
        }

        /// <summary>
        /// 返回指定 <see paramref="fieldName"/> 对应的值。
        /// </summary>
        /// <typeparam name="TValue">要检索的值的类型。</typeparam>
        /// <param name="record">从中获取值的记录。</param>
        /// <param name="fieldName">字段名称。</param>
        /// <param name="cancellationToken">取消令牌。默认为 <see cref="CancellationToken.None"/>。</param>
        /// <returns>由 <see paramref="fieldName"/> 指定的字段中的值。</returns>
        /// <exception cref="DataException"/>
        /// <remarks>如果给定的 <see paramref="fieldName"/> 不存在，则此函数抛出异常。</remarks>
        public static async Task<TValue> GetValueAsync<TValue>(this DbDataReader record, string fieldName, CancellationToken cancellationToken = default)
        {
            try
            {
                var ordinal = record.GetOrdinal(fieldName);
                return await record.GetFieldValueAsync<TValue>(ordinal, cancellationToken).ConfigureAwait(false);
            }
            catch (IndexOutOfRangeException e)
            {
                throw new DataException($"Field '{fieldName}' not found in data record.", e);
            }
        }



        /// <summary>
        /// 使用反射将给定参数添加到命令中。
        /// </summary>
        /// <typeparam name="T">参数的类型。</typeparam>
        /// <param name="command">命令。</param>
        /// <param name="parameters">参数。</param>
        /// <param name="nameMap">将给定属性名称映射到映射中定义的另一个名称。</param>
        /// <remarks>目前不支持集合参数。不缓存反射结果。</remarks>
        public static void ReflectionParameterProvider<T>(this IDbCommand command, T? parameters, IReadOnlyDictionary<string, string>? nameMap = null)
        {
            if (!EqualityComparer<T>.Default.Equals(parameters, default))
            {
                var properties = parameters!.GetType().GetProperties();
                for (int i = 0; i < properties.Length; ++i)
                {
                    var property = properties[i];
                    var value = property.GetValue(parameters, null);
                    var parameter = command.CreateParameter();
                    parameter.Value = value ?? DBNull.Value;
                    parameter.Direction = ParameterDirection.Input;
                    parameter.ParameterName = nameMap != null && nameMap.ContainsKey(properties[i].Name) ? nameMap[property.Name] : properties[i].Name;
                    parameter.DbType = typeMap[property.PropertyType];

                    command.Parameters.Add(parameter);
                }
            }
        }


        /// <summary>
        /// 根据查询结果创建给定类型的对象。
        /// </summary>
        /// <typeparam name="TResult">要构造的类型。</typeparam>
        /// <param name="record">用于读取结果的记录。</param>
        /// <returns><see typeparam="TResult"/> 类型的对象。</returns>
        /// <remarks>不支持类型为 <em>dynamic</em> 的 <see typeparam="TResult"/>。</remarks>
        public static TResult ReflectionSelector<TResult>(this IDataRecord record)
        {
            // 这样做是为了对值类型进行装箱。
            // 否则，property.SetValue() 会操作结构体的副本，而该副本随后会被垃圾回收。
            // 因此无法设置原始结构体的值。
            object obj = Activator.CreateInstance<TResult>()!;
            var properties = obj.GetType().GetProperties();
            for (int i = 0; i < properties.Length; ++i)
            {
                var rp = record[properties[i].Name];
                if (!Equals(rp, DBNull.Value))
                {
                    properties[i].SetValue(obj, rp, null);
                }
            }

            return (TResult)obj;
        }
    }
}

#nullable restore
