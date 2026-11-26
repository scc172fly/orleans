using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 控制处理致命错误(例如宣布失效的 silo )的接口。
    /// </summary>
    public interface IFatalErrorHandler
    {
        /// <summary>
        /// 确定指定例外是否出乎意料。
        /// </summary>
        /// <param name="exception">例外。</param>
        /// <returns><see langword="true"/> ,如果特定例外情形出乎意料;否则, <see langword="false"/>。</returns>
        bool IsUnexpected(Exception exception);

        /// <summary>
        /// 发生致命例外时被调用
        /// </summary>
        /// <param name="sender">发件人</param>
        /// <param name="context">上下文。</param>
        /// <param name="exception">例外。</param>
        void OnFatalException(object? sender = null, string? context = null, Exception? exception = null);
    }
}
