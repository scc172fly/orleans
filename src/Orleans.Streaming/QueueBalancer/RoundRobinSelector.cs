using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Orleans.Streams
{
    /// <summary>
    /// 使用圆轮 Robin 算法选择器
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal sealed class RoundRobinSelector<T> : IResourceSelector<T>
    {
        private readonly List<T> resources;
        private int lastSelection;
        [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
            Justification = "Random ordering and the starting offset spread queue lease attempts. The lease provider establishes ownership independently of this selection order.")]
        public RoundRobinSelector(IEnumerable<T> resources)
        {
            // 随机随机排列单读收藏
            this.resources = resources.Distinct().OrderBy(_ => Random.Shared.Next()).ToList();
            this.lastSelection = Random.Shared.Next(this.resources.Count);
        }

        public int Count => this.resources.Count;

        /// <summary>
        /// 尝试从资源列表中选择一定的资源计数, 与现有资源不重叠
        /// </summary>
        /// <param name="newSelectionCount"></param>
        /// <param name="existingSelection"></param>
        /// <returns></returns>
        public List<T> NextSelection(int newSelectionCount, List<T> existingSelection)
        {
            var selection = new List<T>(Math.Min(newSelectionCount, this.resources.Count));
            int tries = 0;
            while (selection.Count < newSelectionCount && tries++ < this.resources.Count)
            {
                this.lastSelection = (++this.lastSelection) % (this.resources.Count);
                if (!existingSelection.Contains(this.resources[this.lastSelection]))
                    selection.Add(this.resources[this.lastSelection]);
            }
            return selection;
        }
    }
}
