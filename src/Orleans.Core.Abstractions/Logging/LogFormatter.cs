using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// 将值格式化以供日志记录使用。
    /// </summary>
    public static class LogFormatter
    {
        /// <summary>
        /// 支持的最大日志消息大小。
        /// </summary>
        public const int MAX_LOG_MESSAGE_SIZE = 20000;
        private const string TIME_FORMAT = "HH:mm:ss.fff 'GMT'"; // 示例：09:50:43.341 GMT
        private const string DATE_FORMAT = "yyyy-MM-dd " + TIME_FORMAT; // 示例：2010-09-02 09:50:43.341 GMT，即 UniversalSortableDateTimePattern 的变体。
        private static readonly ConcurrentDictionary<Type, Func<Exception, string>> exceptionDecoders = new ConcurrentDictionary<Type, Func<Exception, string>>();

        /// <summary>
        /// 将 <c>DateTime</c> 对象转换为日志记录子系统使用的可显示日期格式的实用方法。
        /// </summary>
        /// <param name="date">要显示的 <c>DateTime</c> 值。</param>
        /// <returns>输入数据的格式化字符串表示，采用日志记录子系统使用的可显示格式。</returns>
        public static string PrintDate(DateTime date)
        {
            return date.ToString(DATE_FORMAT, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 解析日期。
        /// </summary>
        /// <param name="dateStr">日期字符串。</param>
        /// <returns>解析得到的日期。</returns>
        public static DateTime ParseDate(string dateStr)
        {
            return DateTime.ParseExact(dateStr, DATE_FORMAT, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 将 <c>DateTime</c> 对象转换为日志记录子系统使用的可显示时间格式的实用方法。
        /// </summary>
        /// <param name="date">要显示的 <c>DateTime</c> 值。</param>
        /// <returns>输入数据的格式化字符串表示，采用日志记录子系统使用的可显示格式。</returns>
        public static string PrintTime(DateTime date)
        {
            return date.ToString(TIME_FORMAT, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 将异常转换为可显示格式的实用方法，包括展开并格式化所有嵌套的子表达式。
        /// </summary>
        /// <param name="exception">要显示的异常。</param>
        /// <returns>异常的格式化字符串表示，包括展开并格式化所有嵌套的子表达式。</returns>
        public static string PrintException(Exception? exception)
        {
            if (exception == null)
                return "";

            var sb = new StringBuilder();
            PrintException_Helper(sb, exception, 0);
            return sb.ToString();
        }

        /// <summary>
        /// 为指定的异常类型配置异常解码器。
        /// </summary>
        /// <param name="exceptionType">需要配置解码器的异常类型。</param>
        /// <param name="decoder">解码器。</param>
        public static void SetExceptionDecoder(Type exceptionType, Func<Exception, string> decoder)
        {
            exceptionDecoders.TryAdd(exceptionType, decoder);
        }

        private static void PrintException_Helper(StringBuilder sb, Exception exception, int level)
        {
            if (exception == null) return;

            var message = exceptionDecoders.TryGetValue(exception.GetType(), out var decoder) ? decoder(exception) : exception.Message;
            sb.Append($"{Environment.NewLine}Exc level {level}: {exception.GetType()}: {message}");

            if (exception.StackTrace is { } stack)
            {
                sb.Append($"{Environment.NewLine}{stack}");
            }

            if (exception is ReflectionTypeLoadException typeLoadException)
            {
                var loaderExceptions = typeLoadException.LoaderExceptions;
                if (loaderExceptions == null || loaderExceptions.Length == 0)
                {
                    sb.Append("No LoaderExceptions found");
                }
                else
                {
                    foreach (Exception? inner in loaderExceptions)
                    {
                        if (inner is not null)
                        {
                            // 递归处理所有加载器异常，全部使用同一层级。
                            PrintException_Helper(sb, inner, level + 1);
                        }
                    }
                }
            }
            else if (exception.InnerException != null)
            {
                if (exception is AggregateException { InnerExceptions: { Count: > 1 } innerExceptions })
                {
                    foreach (Exception inner in innerExceptions)
                    {
                        // 递归处理所有内部异常，全部使用同一层级。
                        PrintException_Helper(sb, inner, level + 1);
                    }
                }
                else
                {
                    // 递归处理单个内部异常。
                    PrintException_Helper(sb, exception.InnerException, level + 1);
                }
            }
        }
    }
}
