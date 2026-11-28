using System;
using Orleans.Streams;

namespace Orleans.Configuration
{
    /// <summary>
    /// 流缓存逐出的配置选项。
    /// </summary>
    public class StreamCacheEvictionOptions
    {
        /// <summary>
        /// 获取或设置消息在可以按时间清除之前必须在缓存中保留的最短时间。
        /// </summary>
        public TimeSpan DataMinTimeInCache { get; set; } = DefaultDataMinTimeInCache;

        /// <summary>
        /// <see cref="DataMinTimeInCache"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DefaultDataMinTimeInCache = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 获取或设置缓存中最新消息与最早消息之间的时间差。超过此时间差的较旧消息将从缓存中清除。
        /// </summary>
        public TimeSpan DataMaxAgeInCache { get; set; } = DefaultDataMaxAgeInCache;

        /// <summary>
        /// <see cref="DataMaxAgeInCache"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DefaultDataMaxAgeInCache = TimeSpan.FromMinutes(30);

        /// <summary>
        /// 获取或设置消息元数据（<see cref="StreamSequenceToken"/>）在可以按时间清除之前必须在缓存中保留的最短时间。
        /// 用于在完整消息已被清除时避免缓存未命中。
        /// 设置为 <see langword="null"/> 可禁用此跟踪。
        /// </summary>
        public TimeSpan? MetadataMinTimeInCache { get; set; } = DefaultMetadataMinTimeInCache;

        /// <summary>
        /// <see cref="MetadataMinTimeInCache"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DefaultMetadataMinTimeInCache = DefaultDataMinTimeInCache.Multiply(2);
    }

    /// <summary>
    /// 流统计信息的配置选项。
    /// </summary>
    public class StreamStatisticOptions
    {
        /// <summary>
        /// 获取或设置统计监视器的写入间隔。
        /// 统计信息的生成由活动触发。流不活动时将忽略该间隔。
        /// </summary>
        public TimeSpan StatisticMonitorWriteInterval { get; set; } = DefaultStatisticMonitorWriteInterval;

        /// <summary>
        /// <see cref="StatisticMonitorWriteInterval"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DefaultStatisticMonitorWriteInterval = TimeSpan.FromMinutes(5);
    }
}
