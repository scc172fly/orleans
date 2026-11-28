using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 开销低、不分配内存的秒表，用于计量精度在数十毫秒以内的时长。
    /// </summary>
    internal struct CoarseStopwatch : IEquatable<CoarseStopwatch>
    {
        private long _value;

        /// <summary>
        /// 启动一个新实例。
        /// </summary>
        /// <returns>新的、正在运行的秒表。</returns>
        public static CoarseStopwatch StartNew() => new(GetTimestamp());

        /// <summary>
        /// 启动一个新实例，初始已用时间为指定时长。
        /// </summary>
        /// <returns>新的、正在运行的秒表。</returns>
        public static CoarseStopwatch StartNew(long elapsedMs) => new(GetTimestamp() - elapsedMs);

        /// <summary>
        /// 使用指定时间戳创建新实例。
        /// </summary>
        /// <returns>新的秒表。</returns>
        public static CoarseStopwatch FromTimestamp(long timestamp) => new(timestamp);

        private CoarseStopwatch(long timestamp)
        {
            _value = timestamp;
        }

        /// <summary>
        /// 此秒表每秒的计时刻度数。
        /// </summary>
        public const long Frequency = 1000;

        /// <summary>
        /// 如果此实例正在运行，则返回 true，否则返回 false。
        /// </summary>
        public readonly bool IsRunning => _value > 0;

        /// <summary>
        /// 返回已用时间。
        /// </summary>
        public readonly TimeSpan Elapsed => TimeSpan.FromMilliseconds(ElapsedMilliseconds);

        /// <summary>
        /// 返回一个值，指示此实例是否具有默认值。
        /// </summary>
        public readonly bool IsDefault => _value == 0;

        /// <summary>
        /// 返回已用计时刻度数。
        /// </summary>
        public readonly long ElapsedMilliseconds
        {
            get
            {
                // 正的时间戳值表示正在运行的秒表的开始时间，
                // 负值表示已停止的秒表的总时长的相反数。
                var timestamp = _value;

                long delta;
                if (IsRunning)
                {
                    // 秒表仍在运行。
                    var start = timestamp;
                    var end = GetTimestamp();
                    delta = end - start;
                }
                else
                {
                    // 秒表已停止。
                    delta = -timestamp;
                }

                return delta;
            }
        }

        /// <summary>
        /// 获取计时机制的刻度数。
        /// </summary>
        /// <returns>计时机制的刻度数。</returns>
        public static long GetTimestamp() => Environment.TickCount64;

        /// <summary>
        /// 根据提供的开始和结束时间戳返回一个新的、已停止的 <see cref="CoarseStopwatch"/>。
        /// </summary>
        /// <param name="start">开始时间戳。</param>
        /// <param name="end">结束时间戳。</param>
        /// <returns>根据提供的开始和结束时间戳创建的新的、已停止的 <see cref="CoarseStopwatch"/>。</returns>
        public static CoarseStopwatch FromTimestamp(long start, long end) => new(-(end - start));

        /// <summary>
        /// 获取此实例的原始计数器值。
        /// </summary>
        /// <remarks> 
        /// 正的时间戳值表示正在运行的秒表的开始时间，
        /// 负值表示已停止的秒表的总时长的相反数。
        /// </remarks>
        /// <returns>原始计数器值。</returns>
        public readonly long GetRawTimestamp() => _value;

        /// <summary>
        /// 启动秒表。
        /// </summary>
        public void Start()
        {
            var timestamp = _value;

            // 如果已经启动，则不执行任何操作。
            if (IsRunning) return;

            // 秒表已停止，因此其值为零或负数。
            // 将该负值加到当前时间戳上，从而再次启动秒表。
            var newValue = GetTimestamp() + timestamp;
            if (newValue == 0) newValue = 1;
            _value = newValue;
        }

        /// <summary>
        /// 重新启动此秒表，从已用时间为零开始计时。
        /// </summary>
        public void Restart() => _value = GetTimestamp();

        /// <summary>
        /// 重置此秒表，使其处于已停止状态且已用时长为零。
        /// </summary>
        public void Reset() => _value = 0;

        /// <summary>
        /// 停止此秒表。
        /// </summary>
        public void Stop()
        {
            var timestamp = _value;

            // 如果已经停止，则不执行任何操作。
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
