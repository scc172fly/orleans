using System;
using Orleans.Streams;

namespace Orleans.Configuration
{
    /// <summary>
    /// 流缓存驱逐配置选项 。
    /// </summary>
    public class StreamCacheEvictionOptions
    {
        /// <summary>
        /// 获取或设定信件在时间化清洗前会留在缓存的最小时间 。
        /// </summary>
        public TimeSpan DataMinTimeInCache { get; set; } = DefaultDataMinTimeInCache;

        /// <summary>
        /// <see cref="DataMinTimeInCache"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DefaultDataMinTimeInCache = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 获取或设置缓存中最新和最老信件的时间差。 任何比此旧的信件将从缓存中删除 。
        /// </summary>
        public TimeSpan DataMaxAgeInCache { get; set; } = DefaultDataMaxAgeInCache;

        /// <summary>
        /// <see cref="DataMaxAgeInCache"/>的默认值
        /// </summary>
        public static readonly TimeSpan DefaultDataMaxAgeInCache = TimeSpan.FromMinutes(30);

        /// <summary>
        /// 获取或设定信件元数据(<see cref="StreamSequenceToken"/>)在可以用于基于时间的清除之前将留在缓存中的最短时间 。
        /// 如果全部信件被清除, 用于避免缓存误差 。
        /// 设定为 <see langword="null"/> 以禁用此跟踪 。
        /// </summary>
        public TimeSpan? MetadataMinTimeInCache { get; set; } = DefaultMetadataMinTimeInCache;

        /// <summary>
        /// <see cref="MetadataMinTimeInCache"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DefaultMetadataMinTimeInCache = DefaultDataMinTimeInCache.Multiply(2);
    }

    /// <summary>
    /// 流数据配置选项。
    /// </summary>
    public class StreamStatisticOptions
    {
        /// <summary>
        /// 获取或设置统计监视器的写时间隔。
        /// 生成统计数据是由活动引发的。 当流不活跃时, 间隔将被忽略 。
        /// </summary>
        public TimeSpan StatisticMonitorWriteInterval { get; set; } = DefaultStatisticMonitorWriteInterval;

        /// <summary>
        /// <see cref="StatisticMonitorWriteInterval"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DefaultStatisticMonitorWriteInterval = TimeSpan.FromMinutes(5);
    }
}
