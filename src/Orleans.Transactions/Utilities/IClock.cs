using System;

namespace Orleans.Transactions
{
    /// <summary>
    /// 提供当前协调世界时。
    /// </summary>
    public interface IClock
    {
        /// <summary>
        /// 获取当前协调世界时。
        /// </summary>
        /// <returns>当前 UTC 日期和时间。</returns>
        DateTime UtcNow();
    }
}
