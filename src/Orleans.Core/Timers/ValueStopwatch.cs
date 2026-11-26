using System;
using System.Diagnostics;

namespace Orleans.Runtime
{
    /// <summary>
    /// 不得分配计时时间的停靠观察。
    /// </summary>
    internal struct ValueStopwatch
    {
        private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;
        private long _value;

        /// <summary>
        /// 开始一个新实例。
        /// </summary>
        /// <returns>一个新的正在运行的站哨。</returns>
        public static ValueStopwatch StartNew() => new(GetTimestamp());

        /// <summary>
        /// 启动一个初始过期时间的新实例。
        /// </summary>
        /// <param name="elapsed">
        /// 最初的过期时间。
        /// </param>
        /// <returns>一个新的正在运行的站哨。</returns>
        public static ValueStopwatch StartNew(TimeSpan elapsed) => new(GetTimestamp() - (long)(elapsed.TotalSeconds * Stopwatch.Frequency));

        private ValueStopwatch(long timestamp)
        {
            this._value = timestamp;
        }

        /// <summary>
        /// 如果此实例运行时或出现错误, 返回为真 。
        /// </summary>
        public readonly bool IsRunning => this._value > 0;

        /// <summary>
        /// 返回已过的时间。
        /// </summary>
        public TimeSpan Elapsed => TimeSpan.FromTicks(this.ElapsedTicks);

        /// <summary>
        /// 返回已过秒数。
        /// </summary>
        public readonly long ElapsedTicks
        {
            get
            {
                // 时间戳值为正值,表示运行中站表的起始时间,
                // a 负值表示停止的截表的总负长度为负数。
                var timestamp = this._value;

                long delta;
                if (this.IsRunning)
                {
                    // 站表还在运行
                    var start = timestamp;
                    var end = Stopwatch.GetTimestamp();
                    delta = end - start;
                }
                else
                {
                    // 停止了监测
                    delta = -timestamp;
                }

                return (long)(delta * TimestampToTicks);
            }
        }

        /// <summary>
        /// 获取定时器机制中的滴数。
        /// </summary>
        /// <returns>计时器机制中的计数</returns>
        public static long GetTimestamp() => Stopwatch.GetTimestamp();

        /// <summary>
        /// 返回新的、已停止的 <see cref="ValueStopwatch"/> ,并使用提供的起始和结束时间戳。
        /// </summary>
        /// <param name="start">启动时间戳。</param>
        /// <param name="end">末时间戳。</param>
        /// <returns>一个新的、停止的 <see cref="ValueStopwatch"/> ,加上提供的起始和结束时间戳。</returns>
        public static ValueStopwatch FromTimestamp(long start, long end) => new ValueStopwatch(-(end - start));

        /// <summary>
        /// 获得本例的原始对价值。
        /// </summary>
        /// <remarks> 
        /// 时间戳值为正值,表示运行中站表的起始时间,
        /// a 负值表示停止的截表的总负长度为负数。
        /// </remarks>
        /// <returns>原对数值。</returns>
        public readonly long GetRawTimestamp() => this._value;

        /// <summary>
        /// 启动手表
        /// </summary>
        public void Start()
        {
            var timestamp = this._value;

            // 如果已经开始, 什么都不做 。
            if (this.IsRunning) return;

            // 停止了监视, 因此值为零或负。
            // 在当前时间戳中添加负值以重新开始停止监视 。
            var newValue = GetTimestamp() + timestamp;
            if (newValue == 0) newValue = 1;
            this._value = newValue;
        }

        /// <summary>
        /// 休息表从零时间开始
        /// </summary>
        public void Restart() => this._value = GetTimestamp();

        /// <summary>
        /// 将此停止监视恢复为已停止状态, 且时间不长 。
        /// </summary>
        public void Reset() => this._value = 0;

        /// <summary>
        /// 停止这个手表
        /// </summary>
        public void Stop()
        {
            var timestamp = this._value;

            // 如果已经停止, 什么都不做 。
            if (!this.IsRunning) return;

            var end = GetTimestamp();
            var delta = end - timestamp;

            this._value = -delta;
        }
    }
}
