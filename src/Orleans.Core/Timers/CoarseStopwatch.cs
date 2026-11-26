using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 低劣,不分配截时表 时间长度精确度在数十毫秒内
    /// </summary>
    internal struct CoarseStopwatch : IEquatable<CoarseStopwatch>
    {
        private long _value;

        /// <summary>
        /// 开始一个新实例。
        /// </summary>
        /// <returns>一个新的正在运行的站哨。</returns>
        public static CoarseStopwatch StartNew() => new(GetTimestamp());

        /// <summary>
        /// 启动一个已过期指定期限的新实例。
        /// </summary>
        /// <returns>一个新的正在运行的站哨。</returns>
        public static CoarseStopwatch StartNew(long elapsedMs) => new(GetTimestamp() - elapsedMs);

        /// <summary>
        /// 创建带有指定时间戳的新实例。
        /// </summary>
        /// <returns>新的手表</returns>
        public static CoarseStopwatch FromTimestamp(long timestamp) => new(timestamp);

        private CoarseStopwatch(long timestamp)
        {
            _value = timestamp;
        }

        /// <summary>
        /// 此停靠表的秒数 。
        /// </summary>
        public const long Frequency = 1000;

        /// <summary>
        /// 如果此实例运行时或出现错误, 返回为真 。
        /// </summary>
        public readonly bool IsRunning => _value > 0;

        /// <summary>
        /// 返回已过的时间。
        /// </summary>
        public readonly TimeSpan Elapsed => TimeSpan.FromMilliseconds(ElapsedMilliseconds);

        /// <summary>
        /// 返回一个值,以显示此实例是否有默认值。
        /// </summary>
        public readonly bool IsDefault => _value == 0;

        /// <summary>
        /// 返回已过秒数。
        /// </summary>
        public readonly long ElapsedMilliseconds
        {
            get
            {
                // 时间戳值为正值,表示运行中站表的起始时间,
                // a 负值表示停止的截表的总负长度为负数。
                var timestamp = _value;

                long delta;
                if (IsRunning)
                {
                    // 站表还在运行
                    var start = timestamp;
                    var end = GetTimestamp();
                    delta = end - start;
                }
                else
                {
                    // 停止了监测
                    delta = -timestamp;
                }

                return delta;
            }
        }

        /// <summary>
        /// 获取定时器机制中的滴数。
        /// </summary>
        /// <returns>计时器机制中的计数</returns>
        public static long GetTimestamp() => Environment.TickCount64;

        /// <summary>
        /// 返回新的、已停止的 <see cref="CoarseStopwatch"/> ,并使用提供的起始和结束时间戳。
        /// </summary>
        /// <param name="start">启动时间戳。</param>
        /// <param name="end">末时间戳。</param>
        /// <returns>一个新的、停止的 <see cref="CoarseStopwatch"/> ,加上提供的起始和结束时间戳。</returns>
        public static CoarseStopwatch FromTimestamp(long start, long end) => new(-(end - start));

        /// <summary>
        /// 获得本例的原始对价值。
        /// </summary>
        /// <remarks> 
        /// 时间戳值为正值,表示运行中站表的起始时间,
        /// a 负值表示停止的截表的总负长度为负数。
        /// </remarks>
        /// <returns>原对数值。</returns>
        public readonly long GetRawTimestamp() => _value;

        /// <summary>
        /// 启动手表
        /// </summary>
        public void Start()
        {
            var timestamp = _value;

            // 如果已经开始, 什么都不做 。
            if (IsRunning) return;

            // 停止了监视, 因此值为零或负。
            // 在当前时间戳中添加负值以重新开始停止监视 。
            var newValue = GetTimestamp() + timestamp;
            if (newValue == 0) newValue = 1;
            _value = newValue;
        }

        /// <summary>
        /// 休息表从零时间开始
        /// </summary>
        public void Restart() => _value = GetTimestamp();

        /// <summary>
        /// 将此停止监视恢复为已停止状态, 且时间不长 。
        /// </summary>
        public void Reset() => _value = 0;

        /// <summary>
        /// 停止这个手表
        /// </summary>
        public void Stop()
        {
            var timestamp = _value;

            // 如果已经停止, 什么都不做 。
            if (!IsRunning) return;

            var end = GetTimestamp();
            var delta = end - timestamp;

            _value = -delta;
        }

        public override readonly bool Equals(object? obj) => obj is CoarseStopwatch stopwatch && _value == stopwatch._value;
        public readonly bool Equals(CoarseStopwatch other) => _value == other._value;
        public override readonly int GetHashCode() => HashCode.Combine(_value);
        public static bool operator ==(CoarseStopwatch left, CoarseStopwatch right) => left.Equals(right);
        public static bool operator !=(CoarseStopwatch left, CoarseStopwatch right) => !left.Equals(right);
    }
}
