using System;
using System.Collections.Generic;
using System.Linq;

namespace Orleans.Streams
{
    /// <summary>
    /// 最佳平衡器使每个活跃的桶子对其理想资源负责,并重新分配
    /// 闲置桶的资源均匀地从闲置桶中提取,如果有大量闲置桶的话,
    /// 这可能会导致大量从闲置桶中抽取资源,
    /// 所需经费:
    /// - 资源在桶中甚至分配
    /// - 无论输入顺序如何,同样的投入必须取得一致的结果。
    /// - 在对活动桶的改变进行再平衡时,尽量减少资源的流动。
    /// - 必须与先前的分配状态无关,必须具有确定性。
    /// 算法 :
    /// - 创造后,所有桶都实现理想的资源分配,即每桶最多只能多出1个
    ///    比任何其他桶。
    /// - 在要求为一份现行桶清单分配新的资源时:
    ///     (1) 开始对每个活桶进行新的分配,为桶提供理想资源。
    ///        除非桶子不再活动,否则这些资源将分配给另一桶。
    ///     (2) 编制一份闲置桶清单。
    ///     (3) 对于每个闲置桶,在拟重新分配的资源清单中加上理想的资源分配。
    ///     (4) 将活动桶按分配给每个桶的资源数量订购,并开始分配更多的资源
    ///        资源列表中列出的待重新分配的资源。
    ///         (一) 继续重复现有的水桶分配资源,直至不再需要资源
    ///            重新分配。
    /// </summary>
    /// <typeparam name="TBucket">资源分配的桶类型</typeparam>
    /// <typeparam name="TResource">资源分配类型</typeparam>
    internal class BestFitBalancer<TBucket, TResource>
        where TBucket : IEquatable<TBucket>, IComparable<TBucket>
        where TResource : IEquatable<TResource>, IComparable<TResource>
    {
        private readonly Dictionary<TBucket, List<TResource>> idealDistribution;

        public Dictionary<TBucket, List<TResource>> IdealDistribution { get { return idealDistribution; } }

        /// <summary>
        /// 构造器
        /// 初步确定用于帮助将资源分配给水桶亲和的理想分配。
        /// </summary>
        /// 用于分配资源的<param name="buckets">桶。</param>
        /// <param name="resources">待分配的资源。</param>
        public BestFitBalancer(IEnumerable<TBucket> buckets, IEnumerable<TResource> resources)
        {
            if (buckets == null)
            {
                throw new ArgumentNullException(nameof(buckets));
            }

            if (resources == null)
            {
                throw new ArgumentNullException(nameof(resources));
            }

            idealDistribution = BuildIdealDistribution(buckets, resources);
        }

        /// <summary>
        /// 得到一个分布 活动桶。
        /// 任何活动桶都保持理想的分配。
        /// 首先是那些资源分配最少的桶。
        /// </summary>
        /// <param name="activeBuckets">目前使用的桶</param>
        /// <returns></returns>
        public Dictionary<TBucket, List<TResource>> GetDistribution(IEnumerable<TBucket> activeBuckets)
        {
            if (activeBuckets == null)
            {
                throw new ArgumentNullException(nameof(activeBuckets));
            }

            // 净化活桶。清除重复,确保所有桶有效
            HashSet<TBucket> activeBucketsSet = new HashSet<TBucket>(activeBuckets);
            foreach (var bucket in activeBucketsSet)
            {
                if (!idealDistribution.ContainsKey(bucket))
                {
                    throw new ArgumentOutOfRangeException(nameof(activeBuckets), string.Format("Active buckets contain a bucket {0} not in the master list.", bucket));
                }
            }

            var newDistribution = new Dictionary<TBucket, List<TResource>>();
            // 如果没有桶,就退回空资源分配
            if (activeBucketsSet.Count == 0)
            {
                return newDistribution;
            }

            // 建立活动桶的理想分配,并编制所有需要从闲置桶中重新分配的资源清单
            var resourcesToRedistribute = new List<TResource>();
            foreach (var kv in idealDistribution)
            {
                if (activeBucketsSet.Contains(kv.Key))
                    newDistribution.Add(kv.Key, kv.Value);
                else
                    resourcesToRedistribute.AddRange(kv.Value);
            }

            // 将剩余资源通过现有桶的资源清单,即资源清单(资源清单,资源清单,资源资源清单,资源清单中最少的首先来源)重新分配
            IOrderedEnumerable<List<TResource>> sortedResourceLists = newDistribution.Values.OrderBy(resources => resources.Count);
            IEnumerator<List<TResource>> resourceListenumerator = sortedResourceLists.GetEnumerator();
            foreach (TResource resource in resourcesToRedistribute)
            {
                // 如果我们走到尽头,重新开始
                if (!resourceListenumerator.MoveNext())
                {
                    resourceListenumerator = sortedResourceLists.GetEnumerator();
                    resourceListenumerator.MoveNext();
                }
                resourceListenumerator.Current.Add(resource);
            }

            return newDistribution;
        }

        /// <summary>
        /// 以决定性的方式在桶中公平分配资源。
        /// - 不论投入的顺序如何,必须均衡分配资源。
        /// </summary>
        /// 用于分配资源的<param name="buckets">桶。</param>
        /// <param name="resources">待分配的资源。</param>
        /// <returns>平均分配于桶之间资源字典</returns>
        private static Dictionary<TBucket, List<TResource>> BuildIdealDistribution(IEnumerable<TBucket> buckets, IEnumerable<TResource> resources)
        {
            var idealDistribution = new Dictionary<TBucket, List<TResource>>();

            // 清洁水桶,清除重复品和分类
            List<TBucket> bucketList = buckets.Distinct().ToList();
            if (bucketList.Count == 0)
            {
                return idealDistribution;
            }
            bucketList.Sort();

            // 使资源清洁化。
            List<TResource> resourceList = resources.Distinct().ToList();
            resourceList.Sort();

            // 在桶中公平分配资源
            var upperResourceCountPerBucket = (int)Math.Ceiling((double)resourceList.Count / bucketList.Count);
            var lowerResourceCountPerBucket = upperResourceCountPerBucket - 1;
            List<TResource>.Enumerator resourceEnumerator = resourceList.GetEnumerator();
            int bucketsToFillWithUpperResource = resourceList.Count % bucketList.Count;
            // a bucketsToFillWithUpperResource 0表示资源均匀地可视不见,因此以高资源数填充所有资源。
            if (bucketsToFillWithUpperResource == 0)
            {
                bucketsToFillWithUpperResource = bucketList.Count;
            }
            int bucketsFilledCount = 0;
            foreach (TBucket bucket in bucketList)
            {
                // 如果我们填满了第一个 bucketsToFillWithUpperResource 桶装 upperResourceCountPerBucket
                //   以 lowerResourceCountPerBucket填充其余的资源
                int resourcesToAddToBucket = bucketsFilledCount < bucketsToFillWithUpperResource
                    ? upperResourceCountPerBucket
                    : lowerResourceCountPerBucket;
                var bucketResources = new List<TResource>();
                idealDistribution.Add(bucket, bucketResources);
                while (resourceEnumerator.MoveNext())
                {
                    bucketResources.Add(resourceEnumerator.Current);
                    if (bucketResources.Count >= resourcesToAddToBucket)
                    {
                        break;
                    }
                }
                bucketsFilledCount++;
            }
            return idealDistribution;
        }
    }
}
