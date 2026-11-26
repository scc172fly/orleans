using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Runtime
{
    /// <summary>
    /// 用于报告错误的 Orleans 运行时间所使用的例外类别。
    /// </summary>
    /// <remarks>
    /// 这也是任何更具体例外的基级
    /// 和《公约》所提出 Orleans 运行时间。
    /// </remarks>
    [Serializable]
    [GenerateSerializer]
    public class WrappedException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="WrappedException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        public WrappedException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="WrappedException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列信息
        /// </param>
        /// <param name="context">
        /// 背景。
        /// </param>
        /// <exception cref="SerializationException">类名为 <see langword="null" /> 或 <see cref="P:System.Exception.HResult" /> 为零(0)</exception>
        /// <exception cref="ArgumentNullException"><paramref name="info" /> 是 <see langword="null" />.</exception>
        [Obsolete]
        protected WrappedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            OriginalExceptionType = info.GetString(nameof(OriginalExceptionType));
        }

        /// <summary>
        /// 获取或设置原始例外的类型。
        /// </summary>
        [Id(0)]
        public string? OriginalExceptionType { get; set; }

        /// <inheritdoc/>
        [Obsolete]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(OriginalExceptionType), OriginalExceptionType);
        }

        /// <summary>
        /// 创建 <see cref="WrappedException"/> 类的新实例,并使用所提供的例外的书串跟踪重新丢弃它。
        /// </summary>
        /// <param name="exception">例外。</param>
        [DoesNotReturn]
        public static void CreateAndRethrow(Exception exception)
        {
            var error = exception switch
            {
                WrappedException => exception,
                { } => CreateFromException(exception),
                null => throw new ArgumentNullException(nameof(exception))
            };

            ExceptionDispatchInfo.Throw(error);
        }

        private static WrappedException CreateFromException(Exception exception)
        {
            var originalExceptionType = RuntimeTypeNameFormatter.Format(exception.GetType());
            var detailedMessage = LogFormatter.PrintException(exception);
            var result = new WrappedException(detailedMessage)
            {
                OriginalExceptionType = originalExceptionType,
            };

            if (exception.StackTrace is { } stackTrace)
            {
                ExceptionDispatchInfo.SetRemoteStackTrace(result, exception.StackTrace);
            }

            return result;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{nameof(WrappedException)} OriginalType: {OriginalExceptionType}, Message: {Message}";
        }
    }
}
