
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orleans.Streams
{
    /// <summary>
    /// 一种 <see cref="IQueueFlowController"/>，用于聚合多个其他 <see cref="IQueueFlowController"/> 值。
    /// </summary>
    public class AggregatedQueueFlowController : List<IQueueFlowController>, IQueueFlowController
    {
        private readonly int defaultMaxAddCount;

        /// <summary>
        /// 初始化 <see cref="AggregatedQueueFlowController"/> 类的新实例。
        /// </summary>
        /// <param name="defaultMaxAddCount">默认的最大添加数量，参见 <see cref="IQueueFlowController.GetMaxAddCount"/>。</param>
        public AggregatedQueueFlowController(int defaultMaxAddCount)
        {
            this.defaultMaxAddCount = defaultMaxAddCount;
        }

        /// <inheritdoc/>
        public int GetMaxAddCount()
        {
            return this.Aggregate(defaultMaxAddCount, (count, fc) => Math.Min(count, fc.GetMaxAddCount()));
        }
    }
}
