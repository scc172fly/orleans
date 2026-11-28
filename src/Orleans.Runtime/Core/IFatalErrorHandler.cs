using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 用于控制致命错误（例如 Silo 被宣告失效）处理方式的接口。
    /// </summary>
    public interface IFatalErrorHandler
    {
        /// <summary>
        /// 确定指定异常是否属于意外异常。
        /// </summary>
        /// <param name="exception">异常。</param>
        /// <returns>若指定异常是意外异常，则为 <see langword="true"/>，否则为 <see langword="false"/>。</returns>
        bool IsUnexpected(Exception exception);

        /// <summary>
        /// 发生致命异常时调用。
        /// </summary>
        /// <param name="sender">发送方。</param>
        /// <param name="context">上下文。</param>
        /// <param name="exception">异常。</param>
        void OnFatalException(object? sender = null, string? context = null, Exception? exception = null);
    }
}
