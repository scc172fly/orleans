using System;

namespace Orleans.EventSourcing
{

    /// <summary>
    /// 表示日志一致性协议内部遇到的连接问题的信息。
    /// 此信息既供协议内部跟踪重试循环，也向希望
    /// 监控其日志一致性 Grain 通信问题的用户公开。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public abstract class ConnectionIssue
    {
        /// <summary>
        /// 最近一次观察到此问题的 UTC 时间戳。
        /// </summary>
        [Id(0)]
        public DateTime TimeStamp { get; set; }

        /// <summary>
        /// 首次观察到此问题的 UTC 时间戳。
        /// </summary>
        [Id(1)]
        public DateTime TimeOfFirstFailure { get; set; }

        /// <summary>
        /// 自第一次失败以来观察到此问题的次数。
        /// </summary>
        [Id(2)]
        public int NumberOfConsecutiveFailures { get; set; }

        /// <summary>
        /// 下次重试前等待的时长。
        /// </summary>
        [Id(3)]
        public TimeSpan RetryDelay { get; set; }

        /// <summary>
        /// 根据其余信息计算重试延迟。表示特定问题类别的子类
        /// 重写此方法。
        /// </summary>
        /// <param name="previous">之前使用的重试延迟。</param>
        /// <returns></returns>
        public abstract TimeSpan ComputeRetryDelay(TimeSpan? previous);
    }
}
