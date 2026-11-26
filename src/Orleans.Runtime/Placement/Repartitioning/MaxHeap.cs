using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Orleans.Placement.Repartitioning;

namespace Orleans.Runtime.Placement.Repartitioning;

internal enum VertexLocation
{
    Unknown,
    Local,
    Remote
}

[DebuggerDisplay("{Vertex} @ {Location}")]
internal sealed class CandidateVertexHeapElement(CandidateVertex value) : IHeapElement<CandidateVertexHeapElement>
{
    public CandidateVertex Vertex { get; } = value;
    public List<(CandidateVertexHeapElement Element, long TransferScore)> ConnectedVertices { get; } = [];
    public GrainId Id => Vertex.Id;
    public long AccumulatedTransferScore { get => Vertex.AccumulatedTransferScore; set => Vertex.AccumulatedTransferScore = value; }
    public VertexLocation Location { get; set; }
    int IHeapElement<CandidateVertexHeapElement>.HeapIndex { get; set; } = -1;
    int IHeapElement<CandidateVertexHeapElement>.CompareTo(CandidateVertexHeapElement other) => AccumulatedTransferScore.CompareTo(other.AccumulatedTransferScore);
}

internal interface IHeapElement<TElement> where TElement : notnull
{
    int HeapIndex { get; set; }
    int CompareTo(TElement other);
}

/// <summary>
///  代表最大堆积物。
/// </summary>
/// <typeparam name="TElement">指定堆积中元素的类型。</typeparam>
/// <remarks>
///  执行一个以阵列支持的四面形最大堆积。
///  最优先的元素先删除。
///  注:这是基于 .NET的 PriorityQueue: https://github.com/dotnet/runtime/blob/e78b72b1fdf43d9678877400bcfe801b38c14681/src/libraries/System.Collections/src/System/Collections/Generic/PriorityQueue.cs。
/// </remarks>
[DebuggerDisplay("Count = {Count}")]
internal sealed class MaxHeap<TElement> where TElement : notnull, IHeapElement<TElement>
{
    /// <summary>
    /// 表示一个内含堆积定序的全干枯树,以数组形式储存。
    /// </summary>
    private readonly TElement?[] _nodes;

    /// <summary>
    /// 堆积中的节点数 。
    /// </summary>
    private int _size;

    /// <summary>
    /// 指定 d-ary 堆积的乘以, 这里为四。
    /// 假定这一价值是2的功率。
    /// </summary>
    private const int Arity = 4;

    /// <summary>
    /// 二进制对数 <see cref="Arity" />。
    /// </summary>
    private const int Log2Arity = 2;

#if DEBUG
    static MaxHeap()
    {
        Debug.Assert(Log2Arity > 0 && Math.Pow(2, Log2Arity) == Arity);
    }
#endif

    /// <summary>
    ///  初始化 <see cref="MaxHeap{TElement}"/> 类的新实例
    ///  包含特定元素和优先事项。
    /// </summary>
    /// <param name="items">用于填充队列的元素和优先级对齐。</param>
    /// <exception cref="ArgumentNullException">
    ///  指定的 <paramref name="items"/> 参数为 <see langword="null"/>。
    /// </exception>
    /// <remarks>
    ///  使用加压操作构造堆积,
    ///  通常比按顺序排列个别元素的排队速度要快。
    /// </remarks>
    public MaxHeap(List<TElement> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _size = items.Count;
        var nodes = new TElement[_size];

        var i = 0;
        foreach (var item in items)
        {
            nodes[i] = item;
            Debug.Assert(item.HeapIndex == -1);
            item.HeapIndex = i;
            i++;
        }

        _nodes = nodes;
        if (_size > 1)
        {
            Heapify();
        }
        else if (_size == 1)
        {
            _nodes[0]!.HeapIndex = 0;
        }
    }

    /// <summary>
    ///  获取 <see cref="MaxHeap{TElement}"/>中包含的元素数。
    /// </summary>
    public int Count => _size;

    public TElement? FirstOrDefault() => _size > 0 ? _nodes[0] : default;

    public bool TryPeek([NotNullWhen(true)] out TElement value)
    {
        if (_size > 0)
        {
            value = _nodes[0]!;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>
    ///  返回 <see cref="MaxHeap{TElement}"/> 的最大元素,但不删除该元素。
    /// </summary>
    /// <exception cref="InvalidOperationException"> <see cref="MaxHeap{TElement}"/> 是空的。</exception>
    /// <returns> <see cref="MaxHeap{TElement}"/>的最大要素。</returns>
    public TElement Peek()
    {
        if (_size == 0)
        {
            throw new InvalidOperationException("Collection is empty.");
        }

        return _nodes[0]!;
    }

    public bool TryPop([NotNullWhen(true)] out TElement value)
    {
        if (_size > 0)
        {
            value = Pop();
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>
    ///  从 <see cref="MaxHeap{TElement}"/>删除最大值元素并返回该元素。
    /// </summary>
    /// <exception cref="InvalidOperationException">队列为空。</exception>
    /// <returns> <see cref="MaxHeap{TElement}"/>的最大要素。</returns>
    public TElement Pop()
    {
        if (_size == 0)
        {
            throw new InvalidOperationException("Collection is empty.");
        }

        var element = _nodes[0]!;
        RemoveRootNode();
        element.HeapIndex = -1;
        return element;

        void RemoveRootNode()
        {
            var lastNodeIndex = --_size;

            if (lastNodeIndex > 0)
            {
                var lastNode = _nodes[lastNodeIndex]!;
                MoveDown(lastNode, 0);
            }

            if (RuntimeHelpers.IsReferenceOrContainsReferences<TElement>())
            {
                _nodes[lastNodeIndex] = default!;
            }
        }
    }

    /// <summary>
    /// 获取元素父系的索引。
    /// </summary>
    private static int GetParentIndex(int index) => (index - 1) >> Log2Arity;

    /// <summary>
    /// 获取元素第一个子孙的索引。
    /// </summary>
    private static int GetFirstChildIndex(int index) => (index << Log2Arity) + 1;

    public void OnDecreaseElementPriority(TElement element)
    {
        // 如果元素已经从堆积中移除, 则此选项为无操作 。
        if (element.HeapIndex < 0)
        {
            return;
        }

        // 元素的优先级已经降低, 所以根据需要将其往下移 以恢复堆积属性 。
        MoveDown(element, element.HeapIndex);
    }

    public void OnIncreaseElementPriority(TElement element)
    {
        // 如果元素已经从堆积中移除, 则此选项为无操作 。
        if (element.HeapIndex <= 0)
        {
            return;
        }

        // 元素的优先性已经提高, 所以根据需要将其移到下方 以恢复堆积物产。
        MoveUp(element, element.HeapIndex);
    }

    /// <summary>
    /// 将未排序列表转换成堆。
    /// </summary>
    public void Heapify()
    {
        // 树叶的叶子,实际上是一分一毫,
        // 不需要纠正这些财产。
        // 仅用于更高的节点, 从第一个有孩子的节点开始。
        // 它是数组中最后一个元素的母值。

        var nodes = _nodes;
        var lastParentWithChildren = GetParentIndex(_size - 1);
        for (var index = lastParentWithChildren; index >= 0; --index)
        {
            MoveDown(nodes[index]!, index);
        }
    }

    /// <summary>
    /// 以指定顺序获取此收藏中的元素。
    /// </summary>
    public UnorderedElementEnumerable UnorderedElements => new(this);

    /// <summary>
    /// 将树上的节点上移以恢复堆积顺序。
    /// </summary>
    private void MoveUp(TElement node, int nodeIndex)
    {
        Debug.Assert(0 <= nodeIndex && nodeIndex < _size);

        var nodes = _nodes;

        while (nodeIndex > 0)
        {
            var parentIndex = GetParentIndex(nodeIndex);
            var parentNode = nodes[parentIndex]!;

            if (node.CompareTo(parentNode) <= 0)
            {
                // 父母比当前节点大。
                break;
            }

            nodes[nodeIndex] = parentNode;
            parentNode.HeapIndex = nodeIndex;
            nodeIndex = parentIndex;
        }

        nodes[nodeIndex] = node;
        node.HeapIndex = nodeIndex;
    }

    /// <summary>
    /// 将树上的节点下移以恢复堆积顺序。
    /// </summary>
    private void MoveDown(TElement node, int nodeIndex)
    {
        // 移动下移的节点不会每次被互换。
        // 相反,受影响道路上的价值观将提升,从而留下一个自由的场所
        // 。插入类中的优化与插入类相似。

        Debug.Assert(0 <= nodeIndex && nodeIndex < _size);

        var nodes = _nodes;
        var size = _size;

        int i;
        while ((i = GetFirstChildIndex(nodeIndex)) < size)
        {
            // 找到孩子的最大优先级的节点
            var maxChild = nodes[i]!;
            var maxChildIndex = i;

            var childIndexUpperBound = Math.Min(i + Arity, size);
            while (++i < childIndexUpperBound)
            {
                var nextChild = nodes[i]!;
                if (nextChild.CompareTo(maxChild) > 0)
                {
                    maxChild = nextChild;
                    maxChildIndex = i;
                }
            }

            // 满足了堆积属性;在此位置插入节点。
            if (node.CompareTo(maxChild) >= 0)
            {
                break;
            }

            // 将最大子节点向上移动
            // 继续从它的位置重复运行。
            nodes[nodeIndex] = maxChild;
            maxChild.HeapIndex = nodeIndex;
            nodeIndex = maxChildIndex;
        }

        nodes[nodeIndex] = node;
        node.HeapIndex = nodeIndex;
    }

    /// <summary>
    ///  列出 <see cref="MaxHeap{TElement}"/>的元素和优先配对
    ///  没有任何命令保证。
    /// </summary>
    public struct UnorderedElementEnumerable : IEnumerator<TElement>, IEnumerable<TElement>
    {
        private readonly MaxHeap<TElement> _heap;
        private int _index;
        private TElement? _current;

        internal UnorderedElementEnumerable(MaxHeap<TElement> heap)
        {
            _heap = heap;
            _index = 0;
            _current = default;
        }

        /// <summary>
        /// <see cref="UnorderedElementEnumerable"/>释放所有使用的资源。
        /// </summary>
        public readonly void Dispose() { }

        /// <summary>
        /// 将分子推进到堆积的下一个元素。
        /// </summary>
        /// <returns><see langword="true"/> 如果该分子成功地被推进到下一个元素; <see langword="false"/> 如果该分子已经超过收藏的终点。</returns>
        public bool MoveNext()
        {
            var localHeap = _heap;

            if ((uint)_index < (uint)localHeap._size)
            {
                _current = localHeap._nodes[_index];
                _index++;
                return true;
            }

            return MoveNextRare();
        }

        private bool MoveNextRare()
        {
            _index = _heap._size + 1;
            _current = default;
            return false;
        }

        /// <summary>
        /// 将元素放在当前点的分子位置。
        /// </summary>
        public readonly TElement Current => _current ?? throw new InvalidOperationException("Current element is not valid.");

        readonly object IEnumerator.Current => Current;

        public readonly UnorderedElementEnumerable GetEnumerator() => this;
        readonly IEnumerator<TElement> IEnumerable<TElement>.GetEnumerator() => this;
        void IEnumerator.Reset()
        {
            _index = 0;
            _current = default;
        }

        readonly IEnumerator IEnumerable.GetEnumerator() => this;
    }
}
