using Orleans.Providers.Streams.Common;
using Xunit;

namespace ServiceBus.Tests.StreamingTests
{
    [TestSuite("BVT")]
    [TestProvider("EventHub")]
    [TestArea("Streaming")]
    public class TimePurgePredicateTests
    {
        private static readonly TimeSpan MinTimeInCache = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan MaxRelitiveAgeInCache = TimeSpan.FromMinutes(30);
        private static readonly TimePurgePredicate TimePurge = new TimePurgePredicate(MinTimeInCache, MaxRelitiveAgeInCache);
        private static readonly DateTime CacheMaxEnqueTime = new DateTime(2000, 3, 12, 10, 13, 32, DateTimeKind.Utc);
        private static readonly DateTime NowUtc = DateTime.UtcNow;

        /// <summary>
        /// 信件在缓存时间不够长,不足以清洗,信息使用年老也不足以清洗
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void TimePurgePredicate_NoPurgeThreshold_Tests()
        {
            DateTime messageEnqueTime = CacheMaxEnqueTime - MaxRelitiveAgeInCache;
            DateTime timeRead = NowUtc - MinTimeInCache;
            TimeSpan timeInCache = NowUtc - timeRead;
            TimeSpan relativeAge = CacheMaxEnqueTime - messageEnqueTime;
            Assert.False(TimePurge.ShouldPurgeFromTime(timeInCache, relativeAge));
        }

        /// <summary>
        /// 信息在暗藏中已经足够长的时间可以清洗, 信息使用年龄也足够老到可以清洗
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void TimePurgePredicate_PurgeDataThreshold_Tests()
        {
            DateTime messageEnqueTime = CacheMaxEnqueTime - MaxRelitiveAgeInCache - TimeSpan.FromTicks(1);
            DateTime timeRead = NowUtc - MinTimeInCache - TimeSpan.FromTicks(1);
            TimeSpan timeInCache = NowUtc - timeRead;
            TimeSpan relativeAge = CacheMaxEnqueTime - messageEnqueTime;
            Assert.True(TimePurge.ShouldPurgeFromTime(timeInCache, relativeAge));
        }

        /// <summary>
        /// 消息在缓存中已足够长的时间可以清洗, 但信件使用年龄不足以清除
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void TimePurgePredicate_NoPurgeAgeThreshold_Tests()
        {
            DateTime messageEnqueTime = CacheMaxEnqueTime - MaxRelitiveAgeInCache;
            DateTime timeRead = NowUtc - MinTimeInCache - TimeSpan.FromTicks(1);
            TimeSpan timeInCache = NowUtc - timeRead;
            TimeSpan relativeAge = CacheMaxEnqueTime - messageEnqueTime;
            Assert.False(TimePurge.ShouldPurgeFromTime(timeInCache, relativeAge));
        }

        /// <summary>
        /// 信件在缓存时间不够长,不足以清洗,但信息使用年老已足够长,足以清洗
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void TimePurgePredicate_NoPurgeTimeInCacheThreshold_Tests()
        {
            DateTime messageEnqueTime = CacheMaxEnqueTime - MaxRelitiveAgeInCache - TimeSpan.FromTicks(1);
            DateTime timeRead = NowUtc - MinTimeInCache;
            TimeSpan timeInCache = NowUtc - timeRead;
            TimeSpan relativeAge = CacheMaxEnqueTime - messageEnqueTime;
            Assert.False(TimePurge.ShouldPurgeFromTime(timeInCache, relativeAge));
        }
    }
}
