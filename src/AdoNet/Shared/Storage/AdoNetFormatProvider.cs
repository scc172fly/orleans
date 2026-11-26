using System;
using System.Globalization;

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
    /// 适合数据库非参数查询中使用的格式 .NET 类型。
    /// </summary>
    internal class AdoNetFormatProvider : IFormatProvider
    {
        private readonly AdoNetFormatter formatter = new AdoNetFormatter();

        /// <summary>
        /// 返回一个要处理的事项实例
        /// </summary>
        /// <param name="formatType">请求格式类型</param>
        /// <returns></returns>
        public object? GetFormat(Type? formatType)
        {
            return formatType == typeof(ICustomFormatter) ? formatter : null;
        }


        private class AdoNetFormatter : ICustomFormatter
        {
            public string Format(string? format, object? arg, IFormatProvider? formatProvider)
            {
                //当 T 没有定义值时, 此无效检查也适用于 Nulable<T> 。
                if (arg == null)
                {
                    return "NULL";
                }

                if (arg is string)
                {
                    return "N'" + ((string)arg).Replace("'", "''", StringComparison.Ordinal) + "'";
                }

                if (arg is DateTime)
                {
                    return "'" + ((DateTime)arg).ToString("O") + "'";
                }

                if (arg is DateTimeOffset)
                {
                    return "'" + ((DateTimeOffset)arg).ToString("O") + "'";
                }

                if (arg is IFormattable)
                {
                    return ((IFormattable)arg).ToString(format, CultureInfo.InvariantCulture);
                }

                return arg.ToString()!;
            }
        }
    }
}

#nullable restore
