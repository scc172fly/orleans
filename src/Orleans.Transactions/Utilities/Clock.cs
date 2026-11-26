using System;

namespace Orleans.Transactions
{
    /// <summary>
    /// 提供系统时钟的时间。
    /// </summary>
    public class Clock : IClock
    {
        /// <inheritdoc/>
        public DateTime UtcNow()
        {
            return DateTime.UtcNow;
        }
    }
}
