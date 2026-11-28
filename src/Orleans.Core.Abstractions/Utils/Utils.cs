using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Orleans.Runtime
{
    /// <summary>
    /// Utils 类包含供应用程序和 Grain 代码使用的多种实用方法。
    /// </summary>
    public static partial class Utils
    {
        /// <summary>
        /// 返回描述 IEnumerable 对象集合的易读文本字符串。
        /// </summary>
        /// <typeparam name="T">列表元素的类型。</typeparam>
        /// <param name="collection">要描述的 IEnumerable。</param>
        /// <param name="toString">将元素转换为字符串。如果未指定，则使用 <see cref="object.ToString"/>。</param>
        /// <param name="separator">要使用的分隔符。</param>
        /// <param name="putInBrackets">将元素放在括号内。</param>
        /// <returns>将各元素的字符串描述用方括号包围，
        /// 并用逗号分隔后组成的字符串。</returns>
        public static string EnumerableToString<T>(IEnumerable<T>? collection, Func<T, string>? toString = null,
                                                        string separator = ", ", bool putInBrackets = true)
        {
            if (collection == null)
                return putInBrackets ? "[]" : "null";

            if (collection is ICollection<T> { Count: 0 })
                return putInBrackets ? "[]" : "";

            var enumerator = collection.GetEnumerator();
            if (!enumerator.MoveNext())
                return putInBrackets ? "[]" : "";

            var firstValue = enumerator.Current;
            if (!enumerator.MoveNext())
            {
                return putInBrackets
                    ? toString != null ? $"[{toString(firstValue)}]" : firstValue == null ? "[null]" : $"[{firstValue}]"
                    : toString != null ? toString(firstValue) : firstValue == null ? "null" : (firstValue.ToString() ?? "");
            }

            var sb = new StringBuilder();
            if (putInBrackets) sb.Append('[');

            if (toString != null) sb.Append(toString(firstValue));
            else if (firstValue == null) sb.Append("null");
            else sb.Append($"{firstValue}");

            do
            {
                sb.Append(separator);

                var value = enumerator.Current;
                if (toString != null) sb.Append(toString(value));
                else if (value == null) sb.Append("null");
                else sb.Append($"{value}");
            } while (enumerator.MoveNext());

            if (putInBrackets) sb.Append(']');
            return sb.ToString();
        }

        /// <summary>
        /// 返回描述将对象映射到对象的字典的易读文本字符串。
        /// </summary>
        /// <typeparam name="T1">字典键的类型。</typeparam>
        /// <typeparam name="T2">字典元素的类型。</typeparam>
        /// <param name="dict">要描述的字典。</param>
        /// <param name="toString">将元素转换为字符串。如果未指定，则使用 <see cref="object.ToString"/>。</param>
        /// <param name="separator">要使用的分隔符。如果未指定，则各元素应以换行符分隔。</param>
        /// <returns>将各键值对的字符串描述用逗号分隔，
        /// 并在整体外加上方括号后组成的字符串。
        /// 每个键值对先使用键的字符串描述，
        /// 再使用值的字符串描述，
        /// 二者以“ -> ”分隔，并用花括号包围。</returns>
        public static string DictionaryToString<T1, T2>(ICollection<KeyValuePair<T1, T2>> dict, Func<T2, string?>? toString = null, string? separator = null)
        {
            if (dict == null || dict.Count == 0)
            {
                return "[]";
            }
            if (separator == null)
            {
                separator = Environment.NewLine;
            }
            var sb = new StringBuilder("[");
            var enumerator = dict.GetEnumerator();
            int index = 0;
            while (enumerator.MoveNext())
            {
                var pair = enumerator.Current;
                sb.Append("{");
                sb.Append(pair.Key);
                sb.Append(" -> ");

                string? val;
                if (toString != null)
                    val = toString(pair.Value);
                else
                    val = pair.Value == null ? "null" : pair.Value.ToString();
                sb.Append(val);

                sb.Append("}");
                if (index++ < dict.Count - 1)
                    sb.Append(separator);
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// 格式化时间间隔的各组成值以供显示。
        /// </summary>
        /// <param name="timeSpan">要格式化的时间间隔。</param>
        /// <returns>格式化后的时间间隔。</returns>
        public static string TimeSpanToString(TimeSpan timeSpan)
        {
            //00:03:32.8289777
            return $"{timeSpan.Hours}h:{timeSpan.Minutes}m:{timeSpan.Seconds}s.{timeSpan.Milliseconds}ms";
        }

        /// <summary>
        /// 将持续时间从计时周期（tick）转换为整数毫秒。
        /// </summary>
        /// <param name="ticks">持续时间，以 tick 为单位。</param>
        /// <returns>持续时间，以整数毫秒为单位。</returns>
        public static long TicksToMilliSeconds(long ticks) => ticks / TimeSpan.TicksPerMillisecond;

        /// <summary>
        /// 将平均持续时间从 tick 转换为毫秒。
        /// </summary>
        /// <param name="ticks">平均持续时间，以 tick 为单位。</param>
        /// <returns>平均持续时间，以毫秒为单位。</returns>
        public static float AverageTicksToMilliSeconds(float ticks) => ticks / TimeSpan.TicksPerMillisecond;

        /// <summary>
        /// 将 Uri 解析为 IPEndpoint。
        /// </summary>
        /// <param name="uri">输入的 Uri。</param>
        /// <returns></returns>
        public static System.Net.IPEndPoint? ToIPEndPoint(this Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            return uri.Scheme switch
            {
                "gwy.tcp" => new System.Net.IPEndPoint(System.Net.IPAddress.Parse(uri.Host), uri.Port),
                _ => null,
            };
        }

        /// <summary>
        /// 将 Uri 解析为不包含世代标识的 Silo 地址。
        /// </summary>
        /// <param name="uri">输入的 Uri。</param>
        public static SiloAddress? ToGatewayAddress(this Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            return uri.Scheme switch
            {
                "gwy.tcp" => SiloAddress.New(System.Net.IPAddress.Parse(uri.Host), uri.Port, 0),
                _ => null,
            };
        }

        /// <summary>
        /// 以网关 URI 格式表示 IP 终结点。
        /// </summary>
        /// <param name="ep">输入的 IP 终结点。</param>
        /// <returns></returns>
        public static Uri ToGatewayUri(this System.Net.IPEndPoint ep)
        {
            ArgumentNullException.ThrowIfNull(ep);
            return new($"gwy.tcp://{new SpanFormattableIPEndPoint(ep)}/0");
        }

        /// <summary>
        /// 以网关 URI 格式表示 Silo 地址。
        /// </summary>
        /// <param name="address">输入的 Silo 地址。</param>
        /// <returns></returns>
        public static Uri ToGatewayUri(this SiloAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            return new($"gwy.tcp://{new SpanFormattableIPEndPoint(address.Endpoint)}/{address.Generation}");
        }

        /// <summary>
        /// 执行操作，并抑制该操作抛出的任何异常。
        /// </summary>
        /// <param name="action">要执行的操作。</param>
        public static void SafeExecute(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            try
            {
                action();
            }
            catch { }
        }

        /// <summary>
        /// 执行操作并抑制其抛出的任何异常；如果提供了日志记录器，还会记录该异常。
        /// </summary>
        /// <param name="action">要执行的操作。</param>
        /// <param name="logger">用于记录异常的日志记录器。</param>
        /// <param name="caller">要包含在日志条目中的调用者名称。</param>
        public static void SafeExecute(Action action, ILogger? logger = null, string? caller = null)
        {
            ArgumentNullException.ThrowIfNull(action);

            try
            {
                action();
            }
            catch (Exception exc)
            {
                if (logger != null)
                    LogIgnoredException(logger, exc, caller);
            }
        }

        /// <summary>
        /// 等待任务，并抑制该任务抛出的任何异常。
        /// </summary>
        /// <param name="task">要等待的任务。</param>
        /// <returns>表示该操作的任务。</returns>
        public static async Task SafeExecuteAsync(Task task)
        {
            ArgumentNullException.ThrowIfNull(task);

            try
            {
                await task;
            }
            catch { }
        }

        internal static void LogIgnoredException(ILogger logger, Exception exc, string? caller)
        {
            try
            {
                if (exc is AggregateException { InnerExceptions.Count: 1 })
                {
                    Debug.Assert(exc.InnerException is not null, "AggregateException should have an inner exception.");
                    exc = exc.InnerException;
                }

                LogWarningIgnoringException(logger, exc, new(exc), caller ?? string.Empty);
            }
            catch
            {
                // 现在彻底忽略该异常。
            }
        }

        /// <summary>
        /// 将序列划分为多个批次，在批次达到指定大小时返回该批次。
        /// </summary>
        /// <param name="sequence">要划分为批次的序列。</param>
        /// <param name="batchSize">每个批次的目标元素数。</param>
        /// <typeparam name="T">序列中元素的类型。</typeparam>
        /// <returns>由非空批次组成的序列。</returns>
        public static IEnumerable<List<T>> BatchIEnumerable<T>(this IEnumerable<T> sequence, int batchSize)
        {
            ArgumentNullException.ThrowIfNull(sequence);
            return Enumerate(sequence, batchSize);

            static IEnumerable<List<T>> Enumerate(IEnumerable<T> sequence, int batchSize)
            {
                var batch = new List<T>(batchSize);
                foreach (var item in sequence)
                {
                    batch.Add(item);
                    // 当批次累积到足够的元素时，将其返回。
                    if (batch.Count >= batchSize)
                    {
                        yield return batch; // batch.ToArray();
                        batch = new List<T>(batchSize);
                    }
                }
                if (batch.Count > 0)
                {
                    yield return batch; //batch.ToArray();
                }
            }
        }

        /// <summary>
        /// 获取当前堆栈跟踪，不包含此方法及指定数量的调用方堆栈帧。
        /// </summary>
        /// <param name="skipFrames">需要额外排除的调用方堆栈帧数。</param>
        /// <returns>格式化后的堆栈跟踪。</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string GetStackTrace(int skipFrames = 0)
        {
            skipFrames += 1; // 从堆栈跟踪中排除此方法。
            return new System.Diagnostics.StackTrace(skipFrames).ToString();
        }

        private readonly struct ExceptionTypeLogValue(Exception exc)
        {
            public override string? ToString() => exc.GetType().FullName;
        }

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100325,
            Level = LogLevel.Warning,
            Message = "Ignoring {ExceptionType} exception thrown from an action called by {Caller}."
        )]
        private static partial void LogWarningIgnoringException(ILogger logger, Exception exception, ExceptionTypeLogValue exceptionType, string caller);
    }
}
