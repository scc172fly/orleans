using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Orleans.Streams
{
    /// <summary>
    /// 使用轮询算法的选择器。
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
            // 已去重、随机排序的只读集合。
            this.resources = resources.Distinct().OrderBy(_ => Random.Shared.Next()).ToList();
            this.lastSelection = Random.Shared.Next(this.resources.Count);
        }

        public int Count => this.resources.Count;

        /// <summary>
        /// 尝试从资源列表中选择指定数量的资源，且不与已有选择中的资源重复。
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
