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
/// 表示最大堆。
/// </summary>
/// <typeparam name="TElement">指定堆中元素的类型。</typeparam>
/// <remarks>
/// 实现以数组为后备存储的四叉最大堆。
/// 优先级最低的元素最先移除。
/// 注：基于 .NET 的 PriorityQueue：https://github.com/dotnet/runtime/blob/e78b72b1fdf43d9678877400bcfe801b38c14681/src/libraries/System.Collections/src/System/Collections/Generic/PriorityQueue.cs
/// </remarks>
[DebuggerDisplay("Count = {Count}")]
internal sealed class MaxHeap<TElement> where TElement : notnull, IHeapElement<TElement>
{
    /// <summary>
    /// 表示存储在数组中的隐式、按堆序排列的完全 d 叉树。
    /// </summary>
    private readonly TElement?[] _nodes;

    /// <summary>
    /// 堆中的节点数。
    /// </summary>
    private int _size;

    /// <summary>
    /// 指定 d 叉堆的分支数，此处为四叉堆。
    /// 假定此值为 2 的幂。
    /// </summary>
    private const int Arity = 4;

    /// <summary>
    /// <see cref="Arity" /> 的以 2 为底的对数。
    /// </summary>
    private const int Log2Arity = 2;

#if DEBUG
    static MaxHeap()
    {
        Debug.Assert(Log2Arity > 0 && Math.Pow(2, Log2Arity) == Arity);
    }
#endif

    /// <summary>
    /// 初始化 <see cref="MaxHeap{TElement}"/> 类的新实例，
    /// 并用指定的元素及优先级填充。
    /// </summary>
    /// <param name="items">用于填充队列的元素与优先级对。</param>
    /// <exception cref="ArgumentNullException">
    /// 指定的 <paramref name="items"/> 参数为 <see langword="null"/>。
    /// </exception>
    /// <remarks>
    /// 通过堆化操作构建堆，
    /// 通常比逐个将元素依次入队更快。
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
    /// 获取 <see cref="MaxHeap{TElement}"/> 中包含的元素数量。
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
    /// 返回 <see cref="MaxHeap{TElement}"/> 中的最大元素，但不移除它。
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="MaxHeap{TElement}"/> 为空。</exception>
    /// <returns><see cref="MaxHeap{TElement}"/> 中的最大元素。</returns>
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
    /// 移除并返回 <see cref="MaxHeap{TElement}"/> 中的最大元素。
    /// </summary>
    /// <exception cref="InvalidOperationException">队列为空。</exception>
    /// <returns><see cref="MaxHeap{TElement}"/> 中的最大元素。</returns>
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
    /// 获取元素父节点的索引。
    /// </summary>
    private static int GetParentIndex(int index) => (index - 1) >> Log2Arity;

    /// <summary>
    /// 获取元素第一个子节点的索引。
    /// </summary>
    private static int GetFirstChildIndex(int index) => (index << Log2Arity) + 1;

    public void OnDecreaseElementPriority(TElement element)
    {
        // 如果该元素已从堆中移除，则不执行任何操作。
        if (element.HeapIndex < 0)
        {
            return;
        }

        // 元素优先级已降低，因此根据需要向下移动，以恢复堆性质。
        MoveDown(element, element.HeapIndex);
    }

    public void OnIncreaseElementPriority(TElement element)
    {
        // 如果该元素已从堆中移除，则不执行任何操作。
        if (element.HeapIndex <= 0)
        {
            return;
        }

        // 元素优先级已提高，因此根据需要向下移动，以恢复堆性质。
        MoveUp(element, element.HeapIndex);
    }

    /// <summary>
    /// 将无序列表转换为堆。
    /// </summary>
    public void Heapify()
    {
        // 树的叶节点实际上是仅含一个元素的堆，
        // 因此无需调整。仅需对更高层的节点恢复堆性质，
        // 从第一个具有子节点的节点开始。
        // 此节点是数组最后一个元素的父节点。

        var nodes = _nodes;
        var lastParentWithChildren = GetParentIndex(_size - 1);
        for (var index = lastParentWithChildren; index >= 0; --index)
        {
            MoveDown(nodes[index]!, index);
        }
    }

    /// <summary>
    /// 按指定顺序获取此集合中的元素。
    /// </summary>
    public UnorderedElementEnumerable UnorderedElements => new(this);

    /// <summary>
    /// 将节点沿树向上移动，以恢复堆序。
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
                // 父节点大于当前节点。
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
    /// 将节点沿树向下移动，以恢复堆序。
    /// </summary>
    private void MoveDown(TElement node, int nodeIndex)
    {
        // 要下移的节点实际上不必每次都进行交换。
        // 而是将受影响路径上的值向上移动，从而留下一个空位，
        // 供该值最终落入。这与插入排序中的优化类似。

        Debug.Assert(0 <= nodeIndex && nodeIndex < _size);

        var nodes = _nodes;
        var size = _size;

        int i;
        while ((i = GetFirstChildIndex(nodeIndex)) < size)
        {
            // 找到优先级最大的子节点。
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

            // 已满足堆性质，将节点插入此位置。
            if (node.CompareTo(maxChild) >= 0)
            {
                break;
            }

            // 将最大的子节点向上移动一个层级，
            // 然后从它的位置继续递归处理。
            nodes[nodeIndex] = maxChild;
            maxChild.HeapIndex = nodeIndex;
            nodeIndex = maxChildIndex;
        }

        nodes[nodeIndex] = node;
        node.HeapIndex = nodeIndex;
    }

    /// <summary>
    /// 枚举 <see cref="MaxHeap{TElement}"/> 中的元素与优先级对，
    /// 不保证任何顺序。
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
        /// 释放 <see cref="UnorderedElementEnumerable"/> 使用的所有资源。
        /// </summary>
        public readonly void Dispose() { }

        /// <summary>
        /// 将枚举器推进到堆中的下一个元素。
        /// </summary>
        /// <returns>如果枚举器成功前进到下一个元素，则为 <see langword="true"/>；如果已越过集合末尾，则为 <see langword="false"/>。</returns>
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
        /// 获取枚举器当前位置的元素。
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
