using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans
{
    /// <summary>
    /// 回调接口 grains 必须安装,以便能够登记和接收提醒通知。
    /// </summary>
    public interface IRemindable : IGrain
    {
        /// <summary>
        /// 接受新的提醒。
        /// </summary>
        /// <param name="reminderName">此提醒器的名称</param>
        /// <param name="status">此提醒的状态 计数</param>
        /// <returns>完成承诺的 grain 将在其处理完成此提醒项后解决。</returns>
        [Alias("ReceiveReminder")]
        Task ReceiveReminder(string reminderName, Runtime.TickStatus status);

        /// <summary>
        /// 接受新的提醒。
        /// </summary>
        /// <param name="reminderName">此提醒器的名称</param>
        /// <param name="status">此提醒的状态 计数</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>完成承诺的 grain 将在其处理完成此提醒项后解决。</returns>
        [Alias("6461BF2F")]
        Task ReceiveReminder(string reminderName, Runtime.TickStatus status, CancellationToken cancellationToken)
            => ReceiveReminder(reminderName, status);
    }

    namespace Runtime
    {
        /// <summary>
        /// 你们应当为教诲而专心致志。
        /// </summary>
        public interface IGrainReminder
        {
            /// <summary>
            /// 获取提醒的名称 。
            /// </summary>
            string ReminderName { get; }
        }

        /// <summary>
        /// 当将滴答交付登记官时滴答状态 grain。
        /// 如果失败, 可能会发生没有按时交付一个滴子。 应用程序可以注意到以下的这种误切滴子 。
        /// 收到一个滴子后,该应用程序可以计算提醒开始后的字数: 
        /// curCount = (现在 - FirstTickTime)/期
        /// 应用程序可以将它记录为“ 计数 ” 。 收到一个滴答后, 误选的勾数 = curCount - 计数 - 1
        /// 此后,应用程序可以设定数 curCount
        /// </summary>
        [Serializable, GenerateSerializer, Immutable]
        public readonly struct TickStatus : IEquatable<TickStatus>
        {
            /// <summary>
            /// 获取提醒的第一个提示到期或被触发的时间 。
            /// </summary>
            [Id(0)]
            public DateTime FirstTickTime { get; }

            /// <summary>
            /// 得到的时期提醒。
            /// </summary>
            [Id(1)]
            public TimeSpan Period { get; }

            /// <summary>
            /// 时间在运行时 silo 时 silo 启动此选项的交付 。
            /// </summary>
            [Id(2)]
            public DateTime CurrentTickTime { get; }

            /// <summary>
            /// 创建新的 <see cref="TickStatus"/> 实例。
            /// </summary>
            /// <param name="firstTickTime">提醒的第一个秒应发时间。</param>
            /// <param name="period">即教诲的时候。</param>
            /// <param name="timeStamp">启动当前滴答的交付时间。</param>
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
            /// 确定两个提醒勾状态是否相等。
            /// </summary>
            public static bool operator ==(TickStatus left, TickStatus right) => left.Equals(right);

            /// <summary>
            /// 确定两个提醒勾选状态是否不平等。
            /// </summary>
            public static bool operator !=(TickStatus left, TickStatus right) => !left.Equals(right);

            /// <inheritdoc/>
            public override string ToString() => $"<{FirstTickTime}, {Period}, {CurrentTickTime}>";
        }

        /// <summary>
        /// 与 Orleans 提醒功能或提醒服务有关的例外。
        /// </summary>
        [Serializable, GenerateSerializer]
        public sealed class ReminderException : OrleansException
        {
            /// <summary>
            /// 初始化 <see cref="ReminderException"/> 类的新实例。
            /// </summary>
            /// <param name="message">电文。</param>
            public ReminderException(string message) : base(message) { }

            /// <summary>
            /// 初始化 <see cref="ReminderException"/> 类的新实例。
            /// </summary>
            /// <param name="info">序列号信息。</param>
            /// <param name="context">上下文。</param>
            [Obsolete]
            public ReminderException(SerializationInfo info, StreamingContext context)
                : base(info, context)
            {
            }
        }
    }
}
