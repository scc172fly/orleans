using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans
{
    /// <summary>
    /// Grain 必须实现此回调接口，才能注册和接收提醒。
    /// </summary>
    public interface IRemindable : IGrain
    {
        /// <summary>
        /// 接收一次新的提醒。
        /// </summary>
        /// <param name="reminderName">此提醒器的名称。</param>
        /// <param name="status">此次提醒器触发的状态。</param>
        /// <returns>表示处理完成的异步操作，Grain 在处理完此次提醒器触发后使其完成。</returns>
        [Alias("ReceiveReminder")]
        Task ReceiveReminder(string reminderName, Runtime.TickStatus status);

        /// <summary>
        /// 接收一次新的提醒。
        /// </summary>
        /// <param name="reminderName">此提醒器的名称。</param>
        /// <param name="status">此次提醒器触发的状态。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示处理完成的异步操作，Grain 在处理完此次提醒器触发后使其完成。</returns>
        [Alias("6461BF2F")]
        Task ReceiveReminder(string reminderName, Runtime.TickStatus status, CancellationToken cancellationToken)
            => ReceiveReminder(reminderName, status);
    }

    namespace Runtime
    {
        /// <summary>
        /// 持久化提醒器的句柄。
        /// </summary>
        public interface IGrainReminder
        {
            /// <summary>
            /// 获取此提醒器的名称。
            /// </summary>
            string ReminderName { get; }
        }

        /// <summary>
        /// 提醒器触发事件投递到注册提醒器的 Grain 时的状态。
        /// 如果出现故障，触发事件可能无法按时投递。应用程序可按以下方法发现遗漏的触发事件。
        /// 收到一次触发事件时，应用程序可以按以下公式计算自提醒器启动以来理论上的触发次数：
        /// curCount = (Now - FirstTickTime) / Period
        /// 应用程序可用 count 记录此前的次数。收到一次触发事件时，遗漏次数 = curCount - count - 1。
        /// 随后应用程序可设置 count = curCount。
        /// </summary>
        [Serializable, GenerateSerializer, Immutable]
        public readonly struct TickStatus : IEquatable<TickStatus>
        {
            /// <summary>
            /// 获取此提醒器首次触发的预定时间或实际触发时间。
            /// </summary>
            [Id(0)]
            public DateTime FirstTickTime { get; }

            /// <summary>
            /// 获取提醒器的触发周期。
            /// </summary>
            [Id(1)]
            public TimeSpan Period { get; }

            /// <summary>
            /// 获取运行时 Silo 开始投递此次触发事件时该 Silo 上的时间。
            /// </summary>
            [Id(2)]
            public DateTime CurrentTickTime { get; }

            /// <summary>
            /// 创建新的 <see cref="TickStatus"/> 实例。
            /// </summary>
            /// <param name="firstTickTime">提醒器首次触发的预定时间。</param>
            /// <param name="period">提醒器的触发周期。</param>
            /// <param name="timeStamp">开始投递此次触发事件的时间。</param>
            /// <returns></returns>
            public TickStatus(DateTime firstTickTime, TimeSpan period, DateTime timeStamp)
            {
                FirstTickTime = firstTickTime;
                Period = period;
                CurrentTickTime = timeStamp;
            }

            /// <inheritdoc/>
            public bool Equals(TickStatus other) =>
                FirstTickTime == other.FirstTickTime
                && Period == other.Period
                && CurrentTickTime == other.CurrentTickTime;

            /// <inheritdoc/>
            public override bool Equals(object? obj) => obj is TickStatus other && Equals(other);

            /// <inheritdoc/>
            public override int GetHashCode() => HashCode.Combine(FirstTickTime, Period, CurrentTickTime);

            /// <summary>
            /// 确定两个提醒器触发状态是否相等。
            /// </summary>
            public static bool operator ==(TickStatus left, TickStatus right) => left.Equals(right);

            /// <summary>
            /// 确定两个提醒器触发状态是否不相等。
            /// </summary>
            public static bool operator !=(TickStatus left, TickStatus right) => !left.Equals(right);

            /// <inheritdoc/>
            public override string ToString() => $"<{FirstTickTime}, {Period}, {CurrentTickTime}>";
        }

        /// <summary>
        /// 与 Orleans 提醒器功能或提醒器服务有关的异常。
        /// </summary>
        [Serializable, GenerateSerializer]
        public sealed class ReminderException : OrleansException
        {
            /// <summary>
            /// 初始化 <see cref="ReminderException"/> 类的新实例。
            /// </summary>
            /// <param name="message">消息。</param>
            public ReminderException(string message) : base(message) { }

            /// <summary>
            /// 初始化 <see cref="ReminderException"/> 类的新实例。
            /// </summary>
            /// <param name="info">序列化信息。</param>
            /// <param name="context">上下文。</param>
            [Obsolete]
            public ReminderException(SerializationInfo info, StreamingContext context)
                : base(info, context)
            {
            }
        }
    }
}
