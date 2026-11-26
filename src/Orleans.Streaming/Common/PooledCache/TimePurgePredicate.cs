
using System;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 确定是否应该根据时间对数据进行清理。
    /// </summary>
    public class TimePurgePredicate
    {
        private readonly TimeSpan minTimeInCache;
        private readonly TimeSpan maxRelativeMessageAge;

        /// <summary>
        /// 初始化 <see cref="TimePurgePredicate"/> 类的新实例。
        /// </summary>
        /// <param name="minTimeInCache">数据应保留在缓存中,除非由于数据大小而清除。</param>
        /// <param name="maxRelativeMessageAge">数据保存在缓存中的最大年龄。</param>
        public TimePurgePredicate(TimeSpan minTimeInCache, TimeSpan maxRelativeMessageAge)
        {
            this.minTimeInCache = minTimeInCache;
            this.maxRelativeMessageAge = maxRelativeMessageAge;
        }

        /// <summary>
        /// 检查是否应该清除信件 。
        /// 如果信件的相对年龄大于 <c>maxRelativeMessageAge</c> ,并且其缓存时间超过 minTimeInCache,则应清除。
        /// </summary>
        /// <param name="timeInCache">消息的量已在此缓存</param>
        /// <param name="relativeAge">相对于最近事件的信息年龄读作</param>
        /// <returns><see langword="true"/> 电文是否应予清除;否则; <see langword="false"/>.</returns>
        public virtual bool ShouldPurgeFromTime(TimeSpan timeInCache, TimeSpan relativeAge)
        {
            // 如果缓存时间超过数据的最低限,且数据年龄大于允许的最大允许年龄,清洗
            return timeInCache > minTimeInCache && relativeAge > maxRelativeMessageAge;
        }
    }
}
