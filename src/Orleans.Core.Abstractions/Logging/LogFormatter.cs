using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// 用于记录目的的格式值。
    /// </summary>
    public static class LogFormatter
    {
        /// <summary>
        /// 支持的最大日志消息大小 。
        /// </summary>
        public const int MAX_LOG_MESSAGE_SIZE = 20000;
        private const string TIME_FORMAT = "HH:mm:ss.fff 'GMT'"; // 示例:09:50:43.341格林尼治标准
        private const string DATE_FORMAT = "yyyy-MM-dd " + TIME_FORMAT; // 示例:2010-09-02 UniversalSorta­bleDateTimePat任
        private static readonly ConcurrentDictionary<Type, Func<Exception, string>> exceptionDecoders = new ConcurrentDictionary<Type, Func<Exception, string>>();

        /// <summary>
        /// 将 <c>DateTime</c> 对象转换为Logger子系统使用的可打印数据格式的实用功能。
        /// </summary>
        /// <param name="date">下 <c>DateTime</c> 要打印的值。</param>
        /// <returns>输入数据的格式字符串表示,以Logger子系统使用的可打印格式。</returns>
        public static string PrintDate(DateTime date)
        {
            return date.ToString(DATE_FORMAT, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 约会
        /// </summary>
        /// <param name="dateStr">日期字符串。</param>
        /// <returns>解析日期。</returns>
        public static DateTime ParseDate(string dateStr)
        {
            return DateTime.ParseExact(dateStr, DATE_FORMAT, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 将一个 <c>DateTime</c> 对象转换为Logger子系统使用的可打印时间格式的实用函数。
        /// </summary>
        /// <param name="date">下 <c>DateTime</c> 要打印的值。</param>
        /// <returns>输入数据的格式字符串表示,以Logger子系统使用的可打印格式。</returns>
        public static string PrintTime(DateTime date)
        {
            return date.ToString(TIME_FORMAT, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 将例外转换为可打印格式的实用功能,包括扩展和格式化任何嵌套子表达式。
        /// </summary>
        /// <param name="exception">要打印的例外。</param>
        /// <returns>格式化的字符串表示例外,包括扩展和格式化任何嵌套子表达式。</returns>
        public static string PrintException(Exception? exception)
        {
            if (exception == null)
                return "";

            var sb = new StringBuilder();
            PrintException_Helper(sb, exception, 0);
            return sb.ToString();
        }

        /// <summary>
        /// 配置指定例外类型的特殊解码器。
        /// </summary>
        /// <param name="exceptionType">用于配置解码器的例外类型。</param>
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
                            // 对所有装载器例外的重复呼叫。 所有都使用相同水平 。
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
                        // 对所有内部例外的重复呼叫。 对于所有其它情况, 都使用相同的级别 。
                        PrintException_Helper(sb, inner, level + 1);
                    }
                }
                else
                {
                    // 重复呼叫单一内部例外。
                    PrintException_Helper(sb, exception.InnerException, level + 1);
                }
            }
        }
    }
}
