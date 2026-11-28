using System;
using System.Diagnostics;

namespace Orleans.Runtime
{
    /// <summary>
    /// 用于计量时长的、不分配内存的秒表。
    /// </summary>
    internal struct ValueStopwatch
    {
        private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;
        private long _value;

        /// <summary>
        /// 启动一个新实例。
        /// </summary>
        /// <returns>新的、正在运行的秒表。</returns>
        public static ValueStopwatch StartNew() => new(GetTimestamp());

        /// <summary>
        /// 使用初始已用时长启动一个新实例。
        /// </summary>
        /// <param name="elapsed">
        /// 初始已用时长。
        /// </param>
        /// <returns>新的、正在运行的秒表。</returns>
        public static ValueStopwatch StartNew(TimeSpan elapsed) => new(GetTimestamp() - (long)(elapsed.TotalSeconds * Stopwatch.Frequency));

        private ValueStopwatch(long timestamp)
        {
            this._value = timestamp;
        }

        /// <summary>
        /// 如果此实例正在运行，则返回 true，否则返回 false。
        /// </summary>
        public readonly bool IsRunning => this._value > 0;

        /// <summary>
        /// 返回已用时间。
        /// </summary>
        public TimeSpan Elapsed => TimeSpan.FromTicks(this.ElapsedTicks);

        /// <summary>
        /// 返回已用计时刻度数。
        /// </summary>
        public readonly long ElapsedTicks
        {
            get
            {
                // 正的时间戳值表示正在运行的秒表的开始时间，
                // 负值表示已停止的秒表的总时长的相反数。
                var timestamp = this._value;

                long delta;
                if (this.IsRunning)
                {
                    // 秒表仍在运行。
                    var start = timestamp;
                    var end = Stopwatch.GetTimestamp();
                    delta = end - start;
                }
                else
                {
                    // 秒表已停止。
                    delta = -timestamp;
                }

                return (long)(delta * TimestampToTicks);
            }
        }

        /// <summary>
        /// 获取计时机制的刻度数。
        /// </summary>
        /// <returns>计时机制的刻度数。</returns>
        public static long GetTimestamp() => Stopwatch.GetTimestamp();

        /// <summary>
        /// 根据提供的开始和结束时间戳返回一个新的、已停止的 <see cref="ValueStopwatch"/>。
        /// </summary>
        /// <param name="start">开始时间戳。</param>
        /// <param name="end">结束时间戳。</param>
        /// <returns>根据提供的开始和结束时间戳创建的新的、已停止的 <see cref="ValueStopwatch"/>。</returns>
        public static ValueStopwatch FromTimestamp(long start, long end) => new ValueStopwatch(-(end - start));

        /// <summary>
        /// 获取此实例的原始计数器值。
        /// </summary>
        /// <remarks> 
        /// 正的时间戳值表示正在运行的秒表的开始时间，
        /// 负值表示已停止的秒表的总时长的相反数。
        /// </remarks>
        /// <returns>原始计数器值。</returns>
        public readonly long GetRawTimestamp() => this._value;

        /// <summary>
        /// 启动秒表。
        /// </summary>
        public void Start()
        {
            var timestamp = this._value;

            // 如果已经启动，则不执行任何操作。
            if (this.IsRunning) return;

            // 秒表已停止，因此其值为零或负数。
            // 将该负值加到当前时间戳上，从而再次启动秒表。
            var newValue = GetTimestamp() + timestamp;
            if (newValue == 0) newValue = 1;
            this._value = newValue;
        }

        /// <summary>
        /// 重新启动此秒表，从已用时间为零开始计时。
        /// </summary>
        public void Restart() => this._value = GetTimestamp();

        /// <summary>
        /// 重置此秒表，使其处于已停止状态且已用时长为零。
        /// </summary>
        public void Reset() => this._value = 0;

        /// <summary>
        /// 停止此秒表。
        /// </summary>
        public void Stop()
        {
            var timestamp = this._value;

            // 如果已经停止，则不执行任何操作。
            if (!this.IsRunning) return;

            var end = GetTimestamp();
            var delta = end - timestamp;

            this._value = -delta;
        }
    }
}
