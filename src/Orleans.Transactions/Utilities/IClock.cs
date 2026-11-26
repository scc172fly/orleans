using System;

namespace Orleans.Transactions
{
    /// <summary>
    /// 提供了当前协调世界时。
    /// </summary>
    public interface IClock
    {
        /// <summary>
        /// 获得当前协调世界时。
        /// </summary>
        /// <returns>当前通用时点日期和时间。</returns>
        DateTime UtcNow();
    }
}
