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
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    /// <summary>
    /// 在非参数化查询中，将 .NET 类型格式化为适合数据库使用的形式。
    /// </summary>
    internal class AdoNetFormatProvider : IFormatProvider
    {
        private readonly AdoNetFormatter formatter = new AdoNetFormatter();

        /// <summary>
        /// 返回格式化器的实例。
        /// </summary>
        /// <param name="formatType">请求的格式类型。</param>
        /// <returns></returns>
        public object? GetFormat(Type? formatType)
        {
            return formatType == typeof(ICustomFormatter) ? formatter : null;
        }


        private class AdoNetFormatter : ICustomFormatter
        {
            public string Format(string? format, object? arg, IFormatProvider? formatProvider)
            {
                // 此 null 检查也适用于 Nullable<T> 中 T 没有定义值的情况。
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
