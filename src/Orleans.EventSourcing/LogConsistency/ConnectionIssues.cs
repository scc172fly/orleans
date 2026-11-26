using System;

namespace Orleans.EventSourcing
{

    /// <summary>
    /// 表示关于日志一致性协议内遇到的连接问题的信息。
    /// 它既用于协议内跟踪回运行环,也用于跟踪回运行循环,并被用户看到 
    /// 监测其通信问题日志一致 grains 的用户。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public abstract class ConnectionIssue
    {
        /// <summary>
        /// 上次观察到这一问题的UTC时间戳
        /// </summary>
        [Id(0)]
        public DateTime TimeStamp { get; set; }

        /// <summary>
        /// 协调世界时日是我们第一次观察到这个问题的时印
        /// </summary>
        [Id(1)]
        public DateTime TimeOfFirstFailure { get; set; }

        /// <summary>
        /// 自第一次失败以来我们观察到这个问题的次数
        /// </summary>
        [Id(2)]
        public int NumberOfConsecutiveFailures { get; set; }

        /// <summary>
        /// 等待下次重试的拖延
        /// </summary>
        [Id(3)]
        public TimeSpan RetryDelay { get; set; }

        /// <summary>
        /// 根据其余信息计算重试延迟。被子类覆盖
        /// 代表特定类别的问题。
        /// </summary>
        /// <param name="previous">以前使用的重试延迟</param>
        /// <returns></returns>
        public abstract TimeSpan ComputeRetryDelay(TimeSpan? previous);
    }
}
