using System;
using System.Collections.Generic;
using System.Linq;

namespace Orleans.Streams
{
    /// <summary>
    /// 最佳适配均衡器使每个活动桶负责其理想资源集合，并将
    /// 非活动桶的资源均匀重新分配给活动桶。如果有大量非活动桶，
    /// 当这些桶重新上线时，可能需要对原属于非活动桶的资源进行较多调动。
    /// 要求：
    /// - 在各桶之间均匀分配资源。
    /// - 对相同输入必须产生一致的结果，不受输入顺序影响。
    /// - 活动桶发生变化而再均衡时，尽量减少资源移动。
    /// - 必须具有确定性，不依赖于先前的分配状态。
    /// 算法：
    /// - 创建时生成所有桶之间的理想资源分布，即每个桶的资源数量与
    /// 其他任意桶相比最多多 1 个。
    /// - 请求为一组活动桶生成新的资源分配时：
    /// 1) 用各活动桶的理想资源初始化其新的分配结果。这样可以确保
    /// 这些资源不会分配给其他桶，除非该桶变为非活动状态。
    /// 2) 构建非活动桶的列表。
    /// 3) 将各非活动桶的理想资源分配加入待重新分配的资源列表。
    /// 4) 按各活动桶已分配的资源数进行排序，然后从
    /// 待重新分配的资源列表中为它们分配更多资源。
    /// i) 继续遍历活动桶并分配资源，直到没有更多资源
    /// 需要重新分配。
    /// </summary>
    /// <typeparam name="TBucket">用于分配资源的桶的类型。</typeparam>
    /// <typeparam name="TResource">要分配的资源的类型。</typeparam>
    internal class BestFitBalancer<TBucket, TResource>
        where TBucket : IEquatable<TBucket>, IComparable<TBucket>
        where TResource : IEquatable<TResource>, IComparable<TResource>
    {
        private readonly Dictionary<TBucket, List<TResource>> idealDistribution;

        public Dictionary<TBucket, List<TResource>> IdealDistribution { get { return idealDistribution; } }

        /// <summary>
        /// 构造函数。
        /// 初始化理想分配，用于维持资源与桶之间的亲和性。
        /// </summary>
        /// <param name="buckets">资源要分配到的桶。</param>
        /// <param name="resources">要分配的资源。</param>
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
        /// 获取活动桶的资源分配结果。
        /// 所有活动桶保留其理想分配。非活动桶的资源均匀重新分配
        /// 给活动桶，从已分配资源最少的桶开始。
        /// </summary>
        /// <param name="activeBuckets">当前活动桶。</param>
        /// <returns></returns>
        public Dictionary<TBucket, List<TResource>> GetDistribution(IEnumerable<TBucket> activeBuckets)
        {
            if (activeBuckets == null)
            {
                throw new ArgumentNullException(nameof(activeBuckets));
            }

            // 规范化活动桶：移除重复项并确保所有桶有效。
            HashSet<TBucket> activeBucketsSet = new HashSet<TBucket>(activeBuckets);
            foreach (var bucket in activeBucketsSet)
            {
                if (!idealDistribution.ContainsKey(bucket))
                {
                    throw new ArgumentOutOfRangeException(nameof(activeBuckets), string.Format("Active buckets contain a bucket {0} not in the master list.", bucket));
                }
            }

            var newDistribution = new Dictionary<TBucket, List<TResource>>();
            // 如果没有桶，则返回空的资源分配结果。
            if (activeBucketsSet.Count == 0)
            {
                return newDistribution;
            }

            // 为活动桶建立理想分配，并构建需要从非活动桶重新分配的所有资源的列表。
            var resourcesToRedistribute = new List<TResource>();
            foreach (var kv in idealDistribution)
            {
                if (activeBucketsSet.Contains(kv.Key))
                    newDistribution.Add(kv.Key, kv.Value);
                else
                    resourcesToRedistribute.AddRange(kv.Value);
            }

            // 将剩余资源重新分配到各活动桶的资源列表中，优先处理资源最少的列表。
            IOrderedEnumerable<List<TResource>> sortedResourceLists = newDistribution.Values.OrderBy(resources => resources.Count);
            IEnumerator<List<TResource>> resourceListenumerator = sortedResourceLists.GetEnumerator();
            foreach (TResource resource in resourcesToRedistribute)
            {
                // 如果到达末尾，则重新从头开始。
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
        /// 以确定性方式将资源均匀分配到各桶。
        /// - 必须均匀分配资源，不受输入顺序影响。
        /// </summary>
        /// <param name="buckets">资源要分配到的桶。</param>
        /// <param name="resources">要分配的资源。</param>
        /// <returns>资源均匀分配到各桶的字典。</returns>
        private static Dictionary<TBucket, List<TResource>> BuildIdealDistribution(IEnumerable<TBucket> buckets, IEnumerable<TResource> resources)
        {
            var idealDistribution = new Dictionary<TBucket, List<TResource>>();

            // 规范化桶：去重并排序。
            List<TBucket> bucketList = buckets.Distinct().ToList();
            if (bucketList.Count == 0)
            {
                return idealDistribution;
            }
            bucketList.Sort();

            // 规范化资源：去重并排序。
            List<TResource> resourceList = resources.Distinct().ToList();
            resourceList.Sort();

            // 将资源均匀分配到各桶。
            var upperResourceCountPerBucket = (int)Math.Ceiling((double)resourceList.Count / bucketList.Count);
            var lowerResourceCountPerBucket = upperResourceCountPerBucket - 1;
            List<TResource>.Enumerator resourceEnumerator = resourceList.GetEnumerator();
            int bucketsToFillWithUpperResource = resourceList.Count % bucketList.Count;
            // bucketsToFillWithUpperResource 为 0 表示资源可以整除，因此所有桶都按资源数量上限填充。
            if (bucketsToFillWithUpperResource == 0)
            {
                bucketsToFillWithUpperResource = bucketList.Count;
            }
            int bucketsFilledCount = 0;
            foreach (TBucket bucket in bucketList)
            {
                // 如果已经用 upperResourceCountPerBucket 个资源填充了前 bucketsToFillWithUpperResource 个桶，
                // 则按 lowerResourceCountPerBucket 填充其余桶。
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
