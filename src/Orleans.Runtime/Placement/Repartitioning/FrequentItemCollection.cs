using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Orleans.Placement.Repartitioning;

namespace Orleans.Runtime.Placement.Repartitioning;

internal sealed class FrequentEdgeCounter(int capacity) : FrequentItemCollection<ulong, Edge>(capacity)
{
    protected override ulong GetKey(in Edge element) => ((ulong)element.Source.Id.GetUniformHashCode()) << 32 | element.Target.Id.GetUniformHashCode();
    public void Clear() => ClearCore();
    public void Remove(in Edge element) => RemoveCore(GetKey(element));
}

// 此处实现论文《Finding top-k elements in data streams》中的 Filtered Space-Saving 算法，
// 作者为 Nuno Homem &amp; Joao Paulo Carvalho（https://www.hlt.inesc-id.pt/~fmmb/references/misnis.ref0a.pdf）。
// 该算法是 Metwally、Agrawal 和 Abbadi 提出的 Space-Saving 算法的改进，
// 原算法见论文《Efficient Computation of Frequent and Top-k Elements in Data Streams》（https://www.cs.emory.edu/~cheung/Courses/584/Syllabus/papers/Frequency-count/2005-Metwally-Top-k-elements.pdf）。
// 此实现使用内联版本的 .NET PriorityQueue，并对其进行了修改，
// 支持增加数值，并使用将键的哈希映射到堆索引的索引结构。
internal abstract class FrequentItemCollection<TKey, TElement>(int capacity) where TElement : notnull where TKey : notnull
{
    /// <summary>
    /// 表示存储在数组中的隐式、按堆序排列的完全 d 叉树。
    /// </summary>
    private Counter[] _heap = [];

    /// <summary>
    /// 将键的哈希映射到其堆索引的字典。
    /// </summary>
    private readonly Dictionary<TKey, int> _heapIndex = [];

    /// <summary>
    /// 堆中的节点数。
    /// </summary>
    private int _heapSize;

    /// <summary>
    /// 指定 d 叉堆的分支数，此处为四叉堆。
    /// 假定此值为 2 的幂。
    /// </summary>
    private const int Arity = 4;

    /// <summary>
    /// <see cref="Arity" /> 的以 2 为底的对数。
    /// </summary>
    private const int Log2Arity = 2;

    /// <summary>
    /// 保存未被跟踪的键的估计计数，以键的哈希作为索引。
    /// 预计会发生哈希冲突。
    /// </summary>
    private readonly uint[] _sketch = new uint[GetSketchSize(capacity)];

    /// <summary>
    /// 获取 <see cref="FrequentItemCollection{TKey,TElement}"/> 中包含的元素数量。
    /// </summary>
    public int Count => _heapSize;

    /// <summary>
    /// 获取 <see cref="FrequentItemCollection{TKey, TElement}"/> 将跟踪的元素数量。
    /// </summary>
    public int Capacity { get; } = capacity;

#if DEBUG
    static FrequentItemCollection()
    {
        Debug.Assert(Log2Arity > 0 && Math.Pow(2, Log2Arity) == Arity);
    }
#endif

    /// <summary>
    /// 返回最多包含 <see cref="Capacity"/> 个键及其估计计数的集合，不保证顺序。
    /// </summary>
    public ElementEnumerator Elements => new(this);

    protected abstract TKey GetKey(in TElement element);

    public void Add(in TElement element)
    {
        const int Increment = 1;
        var nodeIndexHash = GetKey(element);

        // 增加已被跟踪的键的计数。
        // 哈希冲突发生的概率极小，认为可以接受并忽略。
        if (_heapIndex.TryGetValue(nodeIndexHash, out var index))
        {
            ref var counter = ref _heap[index];
            counter.Count += Increment;
            MoveUpHeap(counter, index, nodeIndexHash);
            return;
        }

        // 该键尚未被跟踪，但可以加入 Top-K，因此将其添加。
        if (Count < Capacity)
        {
            InsertHeap(new Counter(element, Increment, error: 0), nodeIndexHash);
            return;
        }

        var min = _heap[0];

        // 过滤掉估计出现频率低于最小值的值。
        var sketchMask = _sketch.Length - 1;
        var sketchHash = nodeIndexHash.GetHashCode();
        var countEstimate = _sketch[sketchHash & sketchMask];
        if (countEstimate + Increment < min.Count)
        {
            // 增加估计计数。
            _sketch[sketchHash & sketchMask] += Increment;
            return;
        }

        // 从哈希索引中移除最小元素。
        var minIndexHash = GetKey(min.Element);
        _heapIndex.Remove(minIndexHash);

        // 淘汰最小元素时，更新其在概要统计结构（sketch）中的计数器，以提高它
        // 将来通过过滤器的概率。
        var minHash = minIndexHash.GetHashCode();
        _sketch[minHash & sketchMask] = min.Count;

        // 用新元素替换刚移除的最小元素，并向下移动直至到达正确位置。
        MoveDownHeap(new Counter(element, countEstimate + Increment, error: countEstimate), 0, nodeIndexHash);
    }

    /// <summary>
    /// 移除与指定哈希对应的计数器。
    /// </summary>
    /// <param name="key">要移除的值的键。</param>
    /// <returns><see langword="true"/> 表示找到了匹配项并已移除，否则返回 <see langword="false"/>。</returns>
    protected bool RemoveCore(TKey key)
    {
        // 从概要统计结构中移除元素。
        var sketchMask = _sketch.Length - 1;
        var sketchHash = key.GetHashCode();
        _sketch[sketchHash & sketchMask] = 0;

        // 从堆索引中移除元素。
        if (!_heapIndex.Remove(key, out var index))
        {
            return false;
        }

        // 从堆中移除元素。
        var nodes = _heap;
        var newSize = --_heapSize;
        if (index < newSize)
        {
            // 正在移除堆中间的元素。
            // 弹出集合的最后一个元素，从被移除的索引位置开始向下调整堆。
            var lastNode = nodes[newSize];

            MoveDownHeap(lastNode, index, GetKey(lastNode.Element));
        }

        nodes[newSize] = default;

        return true;
    }

    protected void ClearCore()
    {
        Array.Clear(_heap, 0, _heapSize);
        _heapIndex.Clear();
        Array.Clear(_sketch);
        _heapSize = 0;
    }

    private static int GetSketchSize(int capacity)
    {
        // 论文《Finding top-k elements in data streams》第 6 章公式 (24) 中建议的常量。
        // 舍入到最接近的 2 的幂，以避免取模并降低分桶成本。
        const int SketchEntriesPerHeapEntry = 6;

        return 1 << 32 - int.LeadingZeroCount(capacity * SketchEntriesPerHeapEntry);
    }

    /// <summary>
    /// 将指定元素添加到 <see cref="FrequentItemCollection{TKey, TElement}"/>。
    /// </summary>
    /// <param name="element">要添加的元素。</param>
    /// <param name="key">用于在堆中索引该元素的键。</param>
    private void InsertHeap(Counter element, TKey key)
    {
        // 在底层数组末尾逻辑上添加节点。
        // 注意，此时无需实际将要入队的节点放到该位置，
        // 因为这次赋值是多余的。

        var currentSize = _heapSize;

        if (_heap.Length == currentSize)
        {
            GrowHeap(currentSize + 1);
        }

        _heapSize = currentSize + 1;

        MoveUpHeap(element, currentSize, key);
    }

    /// <summary>
    /// 扩展优先队列，使其满足指定的最小容量。
    /// </summary>
    private void GrowHeap(int minCapacity)
    {
        Debug.Assert(_heap.Length < minCapacity);

        const int GrowFactor = 2;
        const int MinimumGrow = 4;

        var newCapacity = GrowFactor * _heap.Length;

        // 允许队列在发生溢出之前增长到最大可能容量（约 20 亿个元素）。
        // 注意，由于使用 (uint) 强制转换，即使 _nodes.Length 发生溢出，此检查也仍然有效。
        if ((uint)newCapacity > Array.MaxLength) newCapacity = Array.MaxLength;

        // 确保满足最小增长量。
        newCapacity = Math.Max(newCapacity, _heap.Length + MinimumGrow);

        // 如果计算出的容量仍小于指定值，则将容量设为原始参数值。
        // 容量超过 Array.MaxLength 时，Array.Resize 将抛出 OutOfMemoryException。
        if (newCapacity < minCapacity) newCapacity = minCapacity;

        Array.Resize(ref _heap, newCapacity);
    }

    /// <summary>
    /// 获取元素父节点的索引。
    /// </summary>
    private static int GetParentIndex(int index) => index - 1 >> Log2Arity;

    /// <summary>
    /// 获取元素第一个子节点的索引。
    /// </summary>
    private static int GetFirstChildIndex(int index) => (index << Log2Arity) + 1;

    /// <summary>
    /// 将节点沿树向上移动，以恢复堆序。
    /// </summary>
    private void MoveUpHeap(Counter node, int nodeIndex, TKey nodeKey)
    {
        // 不会一路交换元素直到根节点，而是采用
        // 类似于插入排序的优化方式。

        Debug.Assert(0 <= nodeIndex && nodeIndex < _heapSize);

        var nodes = _heap;
        var hashIndex = _heapIndex;

        while (nodeIndex > 0)
        {
            var parentIndex = GetParentIndex(nodeIndex);
            var parent = nodes[parentIndex];

            if (node.CompareTo(parent) < 0)
            {
                nodes[nodeIndex] = parent;
                hashIndex[GetKey(parent.Element)] = nodeIndex;
                nodeIndex = parentIndex;
            }
            else
            {
                break;
            }
        }

        nodes[nodeIndex] = node;
        hashIndex[nodeKey] = nodeIndex;
    }

    /// <summary>
    /// 将节点沿树向下移动，以恢复堆序。
    /// </summary>
    private void MoveDownHeap(Counter node, int nodeIndex, TKey nodeKey)
    {
        // 要下移的节点实际上不必每次都进行交换。
        // 而是将受影响路径上的值向上移动，从而留下一个空位，
        // 供该值最终落入。这与插入排序中的优化类似。

        Debug.Assert(0 <= nodeIndex && nodeIndex < _heapSize);

        var nodes = _heap;
        var size = _heapSize;
        var hashIndex = _heapIndex;

        int i;
        while ((i = GetFirstChildIndex(nodeIndex)) < size)
        {
            // 找到优先级最小的子节点。
            var minChild = nodes[i];
            var minChildIndex = i;

            var childIndexUpperBound = Math.Min(i + Arity, size);
            while (++i < childIndexUpperBound)
            {
                var nextChild = nodes[i];
                if (nextChild.CompareTo(minChild) < 0)
                {
                    minChild = nextChild;
                    minChildIndex = i;
                }
            }

            // 已满足堆性质，将节点插入此位置。
            if (node.CompareTo(minChild) <= 0)
            {
                break;
            }

            // 将最小的子节点上移一个层级，然后从它的位置继续递归处理。
            nodes[nodeIndex] = minChild;
            hashIndex[GetKey(minChild.Element)] = nodeIndex;
            nodeIndex = minChildIndex;
        }

        hashIndex[nodeKey] = nodeIndex;
        nodes[nodeIndex] = node;
    }

    private struct Counter(TElement element, uint count, uint error) : IComparable<Counter>
    {
        public readonly TElement Element = element;
        public uint Count = count;
        public uint Error = error;

        public readonly int CompareTo(Counter other) => ((ulong)Count << 32 | uint.MaxValue - Error).CompareTo((ulong)other.Count << 32 | uint.MaxValue - other.Error);

        public override readonly string ToString() => $"{Element}: Count: {Count} Error: {Error}";
    }

    /// <summary>
    /// 枚举 <see cref="FrequentItemCollection{TKey, TElement}"/> 中的元素与优先级对，
    /// 不保证任何顺序。
    /// </summary>
    public struct ElementEnumerator : IEnumerator<(TElement Element, uint Count, uint Error)>, IEnumerable<(TElement Element, uint Count, uint Error)>
    {
        private readonly FrequentItemCollection<TKey, TElement> _heap;
        private int _index;
        private Counter _current;

        internal ElementEnumerator(FrequentItemCollection<TKey, TElement> heap)
        {
            _heap = heap;
            _index = 0;
            _current = default;
        }

        /// <summary>
        /// 释放 <see cref="ElementEnumerator"/> 使用的所有资源。
        /// </summary>
        public readonly void Dispose() { }

        /// <summary>
        /// 将枚举器推进到堆中的下一个元素。
        /// </summary>
        /// <returns>如果枚举器成功前进到下一个元素，则为 <see langword="true"/>；如果已越过集合末尾，则为 <see langword="false"/>。</returns>
        public bool MoveNext()
        {
            var localHeap = _heap;

            if ((uint)_index < (uint)localHeap._heapSize)
            {
                _current = localHeap._heap[_index];
                _index++;
                return true;
            }

            return MoveNextRare();
        }

        private bool MoveNextRare()
        {
            _index = _heap._heapSize + 1;
            _current = default;
            return false;
        }

        /// <summary>
        /// 获取枚举器当前位置的元素。
        /// </summary>
        public readonly (TElement Element, uint Count, uint Error) Current => (_current.Element, _current.Count, _current.Error);

        readonly object IEnumerator.Current => _current;

        void IEnumerator.Reset()
        {
            _index = 0;
            _current = default;
        }

        public readonly ElementEnumerator GetEnumerator() => this;
        readonly IEnumerator<(TElement Element, uint Count, uint Error)> IEnumerable<(TElement Element, uint Count, uint Error)>.GetEnumerator() => this;
        readonly IEnumerator IEnumerable.GetEnumerator() => this;
    }
}
