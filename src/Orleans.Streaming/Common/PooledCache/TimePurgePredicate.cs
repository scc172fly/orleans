
using System;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 根据时间判断是否应清除数据。
    /// </summary>
    public class TimePurgePredicate
    {
        private readonly TimeSpan minTimeInCache;
        private readonly TimeSpan maxRelativeMessageAge;

        /// <summary>
        /// 初始化 <see cref="TimePurgePredicate"/> 类的新实例。
        /// </summary>
        /// <param name="minTimeInCache">数据应保留在缓存中的最短时间，因数据大小限制而清除的情况除外。</param>
        /// <param name="maxRelativeMessageAge">缓存中保留的数据的最大相对存留时长。</param>
        public TimePurgePredicate(TimeSpan minTimeInCache, TimeSpan maxRelativeMessageAge)
        {
            this.minTimeInCache = minTimeInCache;
            this.maxRelativeMessageAge = maxRelativeMessageAge;
        }

        /// <summary>
        /// 检查是否应清除消息。
        /// 如果消息的相对存留时长大于 <c>maxRelativeMessageAge</c>，且在缓存中的存留时间超过 minTimeInCache，则应清除消息。
        /// </summary>
        /// <param name="timeInCache">消息在此缓存中的存留时间。</param>
        /// <param name="relativeAge">消息相对于最近读取事件的存留时长。</param>
        /// <returns>如果应清除消息，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
        public virtual bool ShouldPurgeFromTime(TimeSpan timeInCache, TimeSpan relativeAge)
        {
            // 若在缓存中的时间超过最短保留时间，且数据存留时长超过允许的最大值，则清除。
            return timeInCache > minTimeInCache && relativeAge > maxRelativeMessageAge;
        }
    }
}
