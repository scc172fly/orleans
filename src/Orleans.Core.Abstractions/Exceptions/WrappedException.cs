using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Runtime
{
    /// <summary>
    /// Orleans 运行时用于报告错误的异常类。
    /// </summary>
    /// <remarks>
    /// 它也是 Orleans 运行时抛出的各种更具体异常的
    /// 基类。
    /// </remarks>
    [Serializable]
    [GenerateSerializer]
    public class WrappedException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="WrappedException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 消息。
        /// </param>
        public WrappedException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="WrappedException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列化信息。
        /// </param>
        /// <param name="context">
        /// 上下文。
        /// </param>
        /// <exception cref="SerializationException">类名为 <see langword="null" />，或者 <see cref="P:System.Exception.HResult" /> 为零（0）。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="info" /> 为 <see langword="null" />。</exception>
        [Obsolete]
        protected WrappedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            OriginalExceptionType = info.GetString(nameof(OriginalExceptionType));
        }

        /// <summary>
        /// 获取或设置原始异常的类型。
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
        /// 创建 <see cref="WrappedException"/> 类的新实例，并使用所提供异常的堆栈跟踪重新抛出该实例。
        /// </summary>
        /// <param name="exception">异常。</param>
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
