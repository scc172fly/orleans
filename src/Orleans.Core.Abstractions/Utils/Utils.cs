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
    /// 用户端类包含各种应用实用方法和 grain 代码。
    /// </summary>
    public static partial class Utils
    {
        /// <summary>
        /// 返回一个可读的文字字符串,用于描述一个可以数列收集的对象。
        /// </summary>
        /// <typeparam name="T">列表元素的类型。</typeparam>
        /// <param name="collection">描述的IO数。</param>
        /// <param name="toString">将元素转换为字符串。如果未指定,将会使用 <see cref="object.ToString"/> 。</param>
        /// <param name="separator">要使用的分隔符。</param>
        /// <param name="putInBrackets">将元素放在括号</param>
        /// <returns>以个人字符串描述包装而组装的字符串
        /// 带有方括号的元素,用逗号将其分开。</returns>
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
        /// 返回一个可读的人类文字字符串,该字符串描述将对象映射为对象的字典。
        /// </summary>
        /// <typeparam name="T1">字典键的类型。</typeparam>
        /// <typeparam name="T2">字典元素的类型。</typeparam>
        /// <param name="dict">描述的字典。</param>
        /// <param name="toString">将元素转换为字符串。如果未指定,将会使用 <see cref="object.ToString"/> 。</param>
        /// <param name="separator">要使用的分隔符。 如果未指定, 元素应该由新行分隔。</param>
        /// <returns>以个人字符串描述包装而组装的字符串
        /// 方括号内对等的两条,用逗号分隔。
        /// 每一对关键值代表了每个关键对的符号,作为键的字符串描述,然后是
        /// 数值的字符串描述,
        /// 以 " - > " 分隔,并括号内。</returns>
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
        /// 格式化显示时间间隔的组件值。
        /// </summary>
        /// <param name="timeSpan">格式的时间间隔。</param>
        /// <returns>格式化的时间间隔。</returns>
        public static string TimeSpanToString(TimeSpan timeSpan)
        {
            //00:03:32.8289777
            return $"{timeSpan.Hours}h:{timeSpan.Minutes}m:{timeSpan.Seconds}s.{timeSpan.Milliseconds}ms";
        }

        /// <summary>
        /// 将时间段从秒转换为整毫秒。
        /// </summary>
        /// <param name="ticks">期限,以秒计。</param>
        /// <returns>整个时间长度,整毫秒。</returns>
        public static long TicksToMilliSeconds(long ticks) => ticks / TimeSpan.TicksPerMillisecond;

        /// <summary>
        /// 将平均时间从秒转换为毫秒。
        /// </summary>
        /// <param name="ticks">平均持续时间, 以 tits 计。</param>
        /// <returns>平均持续时间,以毫秒计。</returns>
        public static float AverageTicksToMilliSeconds(float ticks) => ticks / TimeSpan.TicksPerMillisecond;

        /// <summary>
        /// 将乌里作为IPEndpoint分析
        /// </summary>
        /// <param name="uri">输入 Uri</param>
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
        /// 将 Uri 解析为 Silo 地址,不包括生成标识符。
        /// </summary>
        /// <param name="uri">输入 Uri</param>
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
        /// 表示网关 URI 格式中的 IP 端点。
        /// </summary>
        /// <param name="ep">输入 IP 端点</param>
        /// <returns></returns>
        public static Uri ToGatewayUri(this System.Net.IPEndPoint ep)
        {
            ArgumentNullException.ThrowIfNull(ep);
            return new($"gwy.tcp://{new SpanFormattableIPEndPoint(ep)}/0");
        }

        /// <summary>
        /// 表示网关 URI 格式中的 silo 地址。
        /// </summary>
        /// <param name="address">输入 silo 地址</param>
        /// <returns></returns>
        public static Uri ToGatewayUri(this SiloAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            return new($"gwy.tcp://{new SpanFormattableIPEndPoint(address.Endpoint)}/{address.Generation}");
        }

        /// <summary>
        /// 执行动作并禁止它所投的例外。
        /// </summary>
        /// <param name="action">要执行的动作。</param>
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
        /// 执行动作, 在提供日志时记录它丢出的任何例外, 并取消此例外 。
        /// </summary>
        /// <param name="action">要执行的动作。</param>
        /// <param name="logger">用于记录例外的日志器。</param>
        /// <param name="caller">将包含在日志条目中的调用者姓名。</param>
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
        /// 等待一项任务,压制它所投的例外。
        /// </summary>
        /// <param name="task">等待的任务。</param>
        /// <returns>代表操作的任务。</returns>
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
                // 现在真的,真的,真的忽略。
            }
        }

        /// <summary>
        /// 将一个序列分组成批,当每批达到规定大小时即产生。
        /// </summary>
        /// <param name="sequence">分批处理的序列。</param>
        /// <param name="batchSize">每批中元素的目标数。</param>
        /// <typeparam name="T">序列中元素的类型。</typeparam>
        /// <returns>一系列非空批次</returns>
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
                    // 等我们积累的批量够多的时候,就寄出来
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
        /// 获取当前堆栈跟踪,不包含此方法和指定数的调用框架。
        /// </summary>
        /// <param name="skipFrames">排除的额外调用框架数。</param>
        /// <returns>格式化的堆栈跟踪。</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string GetStackTrace(int skipFrames = 0)
        {
            skipFrames += 1; //从堆叠追踪中跳过此方法
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
