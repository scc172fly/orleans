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

// 这是从“在数据流中查找顶点元素” 中“稳定空间”的“功能。
// 以努诺·霍姆 &amp; 乔奥·保罗·卡尔瓦略(https://www.hlt.inesc-id.pt/~fmmb/references/misnis.ref0a.pdf)为首。 
// 反过来,这是对Metwally、Agrawal和Abbadi的“空间拯救”算法的修改。
// “数据流中常见和顶端元素的高效计算”(“数据流中常见和高空要素的高效计算”)中描述(第1段)https://www.cs.emory.edu/~cheung/Courses/584/Syllabus/papers/Frequency-count/2005-Metwally-Top-k-elements.pdf).
// 执行此操作时使用了已修改的内嵌 .NET PriorityQueue 版本
// 支持递增值, 并使用索引绘制关键杂交物到堆积指数 。
internal abstract class FrequentItemCollection<TKey, TElement>(int capacity) where TElement : notnull where TKey : notnull
{
    /// <summary>
    /// 表示一个内含堆积定序的全干枯树,以数组形式储存。
    /// </summary>
    private Counter[] _heap = [];

    /// <summary>
    /// 一种字典,它绘制了堆积中索引的钥匙的散列。
    /// </summary>
    private readonly Dictionary<TKey, int> _heapIndex = [];

    /// <summary>
    /// 堆积中的节点数 。
    /// </summary>
    private int _heapSize;

    /// <summary>
    /// 指定 d-ary 堆积的乘以, 这里为四。
    /// 假定这一价值是2的功率。
    /// </summary>
    private const int Arity = 4;

    /// <summary>
    /// 二进制对数 <see cref="Arity" />。
    /// </summary>
    private const int Log2Arity = 2;

    /// <summary>
    /// 包含未跟踪的键的计算估计,按键的散列编制索引。
    /// 预计将发生碰撞。
    /// </summary>
    private readonly uint[] _sketch = new uint[GetSketchSize(capacity)];

    /// <summary>
    /// 获取 <see cref="FrequentItemCollection{TKey,TElement}"/>中包含的元素数。
    /// </summary>
    public int Count => _heapSize;

    /// <summary>
    /// 获取 <see cref="FrequentItemCollection{TKey, TElement}"/> 将跟踪的元素数。
    /// </summary>
    public int Capacity { get; } = capacity;

#if DEBUG
    static FrequentItemCollection()
    {
        Debug.Assert(Log2Arity > 0 && Math.Pow(2, Log2Arity) == Arity);
    }
#endif

    /// <summary>
    /// 返回最多为 <see cref="Capacity"/> 键的集合,并按未说明顺序返回其计算估计数。
    /// </summary>
    public ElementEnumerator Elements => new(this);

    protected abstract TKey GetKey(in TElement element);

    public void Add(in TElement element)
    {
        const int Increment = 1;
        var nodeIndexHash = GetKey(element);

        // 正在跟踪的密钥增加计数 。
        // 发生散列碰撞的可能性很小,这被认为是可以接受和被忽视的。
        if (_heapIndex.TryGetValue(nodeIndexHash, out var index))
        {
            ref var counter = ref _heap[index];
            counter.Count += Increment;
            MoveUpHeap(counter, index, nodeIndexHash);
            return;
        }

        // 键未被跟踪, 但可以安装在顶部 K, 所以添加它 。
        if (Count < Capacity)
        {
            InsertHeap(new Counter(element, Increment, error: 0), nodeIndexHash);
            return;
        }

        var min = _heap[0];

        // 筛选估计显示的数值比最低值少。
        var sketchMask = _sketch.Length - 1;
        var sketchHash = nodeIndexHash.GetHashCode();
        var countEstimate = _sketch[sketchHash & sketchMask];
        if (countEstimate + Increment < min.Count)
        {
            // 增加计算估计数。
            _sketch[sketchHash & sketchMask] += Increment;
            return;
        }

        // 从散列指数中删除最小元素。
        var minIndexHash = GetKey(min.Element);
        _heapIndex.Remove(minIndexHash);

        // 将最低元素除去的同时,在草图中更新其计数器,以增加其机会
        // 未来通过过滤器。
        var minHash = minIndexHash.GetHashCode();
        _sketch[minHash & sketchMask] = min.Count;

        // 将新元素推到最后,然后往下移直到它就位
        MoveDownHeap(new Counter(element, countEstimate + Increment, error: countEstimate), 0, nodeIndexHash);
    }

    /// <summary>
    /// 删除对应指定散列的对应于反面。
    /// </summary>
    /// <param name="key">要删除的值的密钥。</param>
    /// <returns><see langword="true"/> 如果找到并删除匹配条目, <see langword="false"/> 否则。</returns>
    protected bool RemoveCore(TKey key)
    {
        // 从草图中删除元素
        var sketchMask = _sketch.Length - 1;
        var sketchHash = key.GetHashCode();
        _sketch[sketchHash & sketchMask] = 0;

        // 从堆积指数中删除元素
        if (!_heapIndex.Remove(key, out var index))
        {
            return false;
        }

        // 从堆放中删除元素
        var nodes = _heap;
        var newSize = --_heapSize;
        if (index < newSize)
        {
            // 我们正在从堆积的中间 去除一个元素
            // 弹出收藏中的最后元素, 从已删除的索引中向下筛选 。
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
        // 文件中建议的常数“找出数据流中最重的元素”,第6章,方程(24)
        // 圆到最近的电源为 2,用于更便宜的宾式宾客,无模版
        const int SketchEntriesPerHeapEntry = 6;

        return 1 << 32 - int.LeadingZeroCount(capacity * SketchEntriesPerHeapEntry);
    }

    /// <summary>
    ///  将指定元素添加到 <see cref="FrequentItemCollection{TKey, TElement}"/>。
    /// </summary>
    /// <param name="element">要添加的元素。</param>
    /// <param name="key">用于对堆积中元素进行索引的密钥。</param>
    private void InsertHeap(Counter element, TKey key)
    {
        // 几乎在底端数组的末尾添加节点。
        // 注意: 所排的节点不需要实际安装
        // 此时此刻,这种转让是多余的。

        var currentSize = _heapSize;

        if (_heap.Length == currentSize)
        {
            GrowHeap(currentSize + 1);
        }

        _heapSize = currentSize + 1;

        MoveUpHeap(element, currentSize, key);
    }

    /// <summary>
    /// 将优先队列增长到符合指定分钟容量的大小 。
    /// </summary>
    private void GrowHeap(int minCapacity)
    {
        Debug.Assert(_heap.Length < minCapacity);

        const int GrowFactor = 2;
        const int MinimumGrow = 4;

        var newCapacity = GrowFactor * _heap.Length;

        // 允许队列在出现溢出前增长到最大容量(~ 2G元素) 。
        // 请注意,本检查即使在 _nodes.Length 这是由于众人因他而使众人疲倦,
        if ((uint)newCapacity > Array.MaxLength) newCapacity = Array.MaxLength;

        // 确保最低增长得到尊重。
        newCapacity = Math.Max(newCapacity, _heap.Length + MinimumGrow);

        // 如果计算能力仍然低于规定,则与原论点挂钩。
        // 超过 Array.MaxLength 将如 OutOfMemoryException 年 Array.Resize.
        if (newCapacity < minCapacity) newCapacity = minCapacity;

        Array.Resize(ref _heap, newCapacity);
    }

    /// <summary>
    /// 获取元素父系的索引。
    /// </summary>
    private static int GetParentIndex(int index) => index - 1 >> Log2Arity;

    /// <summary>
    /// 获取元素第一个子孙的索引。
    /// </summary>
    private static int GetFirstChildIndex(int index) => (index << Log2Arity) + 1;

    /// <summary>
    /// 将树上的节点上移以恢复堆积顺序。
    /// </summary>
    private void MoveUpHeap(Counter node, int nodeIndex, TKey nodeKey)
    {
        // 与其把项目全部换成根,不如我们继续
        // 与插入类相似的优化。

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
    /// 将树上的节点下移以恢复堆积顺序。
    /// </summary>
    private void MoveDownHeap(Counter node, int nodeIndex, TKey nodeKey)
    {
        // 移动下移的节点不会每次被互换。
        // 相反,受影响道路上的价值观将提升,从而留下一个自由的场所
        // 。插入类中的优化与插入类相似。

        Debug.Assert(0 <= nodeIndex && nodeIndex < _heapSize);

        var nodes = _heap;
        var size = _heapSize;
        var hashIndex = _heapIndex;

        int i;
        while ((i = GetFirstChildIndex(nodeIndex)) < size)
        {
            // 找到孩子的节点, 且给予最小优先
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

            // 满足了堆积属性;在此位置插入节点。
            if (node.CompareTo(minChild) <= 0)
            {
                break;
            }

            // 将最小子上移一个节点,并重现其位置。
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
    ///  列出 <see cref="FrequentItemCollection{TKey, TElement}"/>的元素和优先配对,
    ///  没有任何命令保证。
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
        /// <see cref="ElementEnumerator"/>释放所有使用的资源。
        /// </summary>
        public readonly void Dispose() { }

        /// <summary>
        /// 将分子推进到堆积的下一个元素。
        /// </summary>
        /// <returns><see langword="true"/> 如果该分子成功地被推进到下一个元素; <see langword="false"/> 如果该分子已经超过收藏的终点。</returns>
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
        /// 将元素放在当前点的分子位置。
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
