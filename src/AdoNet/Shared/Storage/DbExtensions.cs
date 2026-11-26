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
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    /// <summary>
    /// 含有一些便利方法,可结合 <see cref="IRelationalStorage">IRelationalStorage</see> 和 <see cref="RelationalStorage">GenericRelationalStorage</see>.
    /// </summary>
    internal static class DbExtensions
    {
        /// <summary>
        /// CLR viz 数据库类型转换的清晰地图。
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
            //使用 DateTime 来交叉 DB 兼容。 基础 DB 表列类型可以是 DateTime 或 DateTime2
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
        /// <typeparam name="T">参数的类型。</typeparam>
        /// <param name="command">创建参数要使用的命令。</param>
        /// <param name="direction">参数的方向。</param>
        /// <param name="parameterName">参数的名称。</param>
        /// <param name="value">参数值。</param>
        /// <param name="size">参数值的大小。</param>
        /// <param name="dbType">排 <see cref="DbType"/> 参数。</param>
        /// <returns>参数,该参数使用给定参数创建。</returns>
        public static IDbDataParameter CreateParameter<T>(this IDbCommand command, ParameterDirection direction, string parameterName, T? value, int? size = null, DbType? dbType = null)
        {
            //价值类型不应有框框。 见:
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
        /// 创建并添加新的 SQL 参数到命令中。
        /// </summary>
        /// <typeparam name="T">参数的类型。</typeparam>
        /// <param name="command">创建参数要使用的命令。</param>
        /// <param name="parameterName">参数的名称。</param>
        /// <param name="value">参数值。</param>
        /// <param name="direction">参数的方向。</param>
        /// <param name="size">参数值的大小。</param>
        /// <param name="dbType">排 <see cref="DbType"/> 参数。</param>
        public static void AddParameter<T>(this IDbCommand command, string parameterName, T? value, ParameterDirection direction = ParameterDirection.Input, int? size = null, DbType? dbType = null)
        {
            command.Parameters.Add(command.CreateParameter(direction, parameterName, value, size, dbType));
        }

        /// <summary>
        /// 如果不是 <see cref="System.DBNull"/>, <em>默认(电视)</em> ,则返回一个数值。
        /// </summary>
        /// <typeparam name="TValue">请求值的类型。</typeparam>
        /// <param name="record">从记录中检索值。</param>
        /// <param name="fieldName">要检索的字段名称。</param>
        /// <param name="default">如果定值为 <see cref="System.DBNull"/>,则默认值。</param>
        /// <returns>要么给定值,要么请求类型中的默认值。</returns>
        /// <remarks>如果给定 <see paramref="fieldName"/> 不存在, 此函数将丢弃 。</remarks>
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
        /// 如果不是 <see cref="System.DBNull"/>、 <em>默认值</em> ,则返回数值。
        /// </summary>
        /// <param name="record">从记录中检索值。</param>
        /// <param name="fieldName">要检索的字段名称。</param>
        /// <param name="default">如果定值为 <see cref="System.DBNull"/>,则默认值。</param>
        /// <returns>要么给定值,要么 <see cref="System.DateTime"/>的默认值?</returns>
        /// <exception cref="DataException"/>
        /// <remarks>在连接器推断出不可取的类型时,需要这种明确功能。
        /// 这里的例子就是 Npgsql.NodaTime,它使 Npgsql 返回 Noda 类型,因此 Orleans 无法
        /// 因为它期望 .NET <see cref="System.DateTime"/>。如果给定 <see paramref="fieldName"/> 不存在, 此函数会丢弃 。</remarks>
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
        /// 如果不是 <see cref="System.DBNull"/>, <em>默认(电视)</em> ,则返回一个数值。
        /// </summary>
        /// <typeparam name="TValue">请求值的类型。</typeparam>
        /// <param name="record">从记录中检索值。</param>
        /// <param name="fieldName">要检索的字段名称。</param>
        /// <param name="default">如果定值为 <see cref="System.DBNull"/>,则默认值。</param>
        /// <returns>要么给定值,要么请求类型中的默认值。</returns>
        /// <exception cref="DataException"/>
        /// <remarks>如果给定 <see paramref="fieldName"/> 不存在, 此函数将丢弃 。</remarks>
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
        /// 如果不是 <see cref="System.DBNull"/>, <em>默认(电视)</em> ,则返回一个数值。
        /// </summary>
        /// <typeparam name="TValue">请求值的类型。</typeparam>
        /// <param name="record">从记录中检索值。</param>
        /// <param name="ordinal">字段名称的正则。</param>
        /// <param name="default">如果定值为 <see cref="System.DBNull"/>,则默认值。</param>
        /// <returns>要么给定值,要么请求类型中的默认值。</returns>
        /// <exception cref="IndexOutOfRangeException"/>                
        public static TValue? GetValueOrDefault<TValue>(this IDataRecord record, int ordinal, TValue? @default = default)
        {
            return record.IsDBNull(ordinal) ? @default : (TValue)record.GetValue(ordinal);
        }


        /// <summary>
        /// 如果不是 <see cref="System.DBNull"/>, <em>默认(电视)</em> ,则返回一个数值。
        /// </summary>
        /// <typeparam name="TValue">请求值的类型。</typeparam>
        /// <param name="record">从记录中检索值。</param>
        /// <param name="ordinal">字段名称的正则。</param>
        /// <param name="default">如果定值为 <see cref="System.DBNull"/>,则默认值。</param>
        /// <returns>要么给定值,要么请求类型中的默认值。</returns>
        /// <exception cref="IndexOutOfRangeException"/>                
        public static async Task<TValue?> GetValueOrDefaultAsync<TValue>(this DbDataReader record, int ordinal, TValue? @default = default)
        {

            return (await record.IsDBNullAsync(ordinal).ConfigureAwait(false)) ? @default : (await record.GetFieldValueAsync<TValue>(ordinal).ConfigureAwait(false));
        }


        /// <summary>
        /// 返回给定 <see paramref="fieldName"/>的值。
        /// </summary>
        /// <typeparam name="TValue">要检索的值类型。</typeparam>
        /// <param name="record">从记录中检索值。</param>
        /// <param name="fieldName">字段的名称。</param>
        /// <returns>指定字段中数值的 <see paramref="fieldName"/>.</returns>
        /// <exception cref="DataException"/>
        /// <remarks>如果给定 <see paramref="fieldName"/> 不存在, 此函数将丢弃 。</remarks>        
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
        /// 回返a <see cref="System.DateTime"/> 给定值的数值 <see paramref="fieldName"/>.
        /// </summary>
        /// <param name="record">从记录中检索值。</param>
        /// <param name="fieldName">字段的名称。</param>
        /// <returns>DateTime 特定字段中的值。</returns>
        /// <exception cref="DataException"/>
        /// <remarks>在连接器推断出不可取的类型时,需要这种明确功能。
        /// 这里的例子就是 Npgsql.NodaTime,它使 Npgsql 返回 Noda 类型,因此 Orleans 无法
        /// 因为它期望 .NET <see cref="System.DateTime"/>。如果给定 <see paramref="fieldName"/> 不存在, 此函数会丢弃 。</remarks>
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
        /// 使用变量文化将给定 <see paramref="fieldName"/> 的值转换为内置。
        /// </summary>
        /// <param name="record">从记录中检索值。</param>
        /// <param name="fieldName">字段的名称。</param>
        /// <exception cref="DataException"/>
        /// <returns>指定字段中的整数值 <see paramref="fieldName"/>.</returns>
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
        /// 返回给定 <see paramref="fieldName"/> 的值,以其长者为准。
        /// </summary>
        /// <param name="record">从记录中检索值。</param>
        /// <param name="fieldName">字段的名称。</param>
        /// <exception cref="DataException"/>
        /// <returns>指定字段中的整数值 <see paramref="fieldName"/>.</returns>
        public static long GetInt64(this IDataRecord record, string fieldName)
        {
            try
            {
                var ordinal = record.GetOrdinal(fieldName);
                // 使用旧的原制式时原始铸造。 这里用于保持后向兼容性
                return record.GetFieldType(ordinal) == typeof(int) ? record.GetInt32(ordinal) : record.GetInt64(ordinal);
            }
            catch (IndexOutOfRangeException e)
            {
                throw new DataException($"Field '{fieldName}' not found in data record.", e);
            }
        }

        /// <summary>
        /// 使用变量文化将给定的 <see paramref="fieldName"/> 值转换为无效内注,
        /// 返回 <see cref="DBNull.Value"/>无效。
        /// </summary>
        /// <param name="record">从记录中检索值。</param>
        /// <param name="fieldName">字段的名称。</param>
        /// <exception cref="DataException"/>
        /// <returns>指定字段中以 <see paramref="fieldName"/>.</returns>
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
        /// 返回给定 <see paramref="fieldName"/>的值。
        /// </summary>
        /// <typeparam name="TValue">要检索的值类型。</typeparam>
        /// <param name="record">从记录中检索值。</param>
        /// <param name="fieldName">字段的名称。</param>
        /// <param name="cancellationToken">取消牌。 默认为 <see cref="CancellationToken.None"/>。</param>
        /// <returns>指定字段中数值的 <see paramref="fieldName"/>.</returns>
        /// <exception cref="DataException"/>
        /// <remarks>如果给定 <see paramref="fieldName"/> 不存在, 此函数将丢弃 。</remarks>        
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
        /// <param name="nameMap">映射地图中定义的另外一个属性名称。</param>
        /// <remarks>不支持当前收藏参数。 不缓存反射结果 。</remarks>
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
        /// 从查询结果中创建给定类型对象。
        /// </summary>
        /// <typeparam name="TResult">要构建的类型。</typeparam>
        /// <param name="record">读取结果的记录。</param>
        /// <returns>类型对象 <see typeparam="TResult"/>.</returns>
        /// <remarks>不支持 <see typeparam="TResult"/> 类型 <em>动态动态</em>.</remarks>
        public static TResult ReflectionSelector<TResult>(this IDataRecord record)
        {
            //这样可以用来框框价值类型。
            //否则 property.SetValue()()将拥有一块支架的复制件,这样
            //获取回收的垃圾。 因此无法设置原始的支架值 。            
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
