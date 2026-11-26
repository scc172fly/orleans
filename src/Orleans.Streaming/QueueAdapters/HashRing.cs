using System;
using Orleans.Runtime;

namespace Orleans.Streams;

internal readonly struct HashRing
{
    private readonly QueueId[] _ring;

    public HashRing(QueueId[] ring)
    {
        Array.Sort(ring, (x, y) => x.GetUniformHashCode().CompareTo(y.GetUniformHashCode()));
        _ring = ring;
    }

    public QueueId[] GetAllRingMembers() => _ring;

    public QueueId CalculateResponsible(uint uniformHashCode)
    {
        // 使用时钟... membershipOracle.CalculateTargetSilo() 中的当前代码会逆时针.
        var index = _ring.AsSpan().BinarySearch(new Searcher(uniformHashCode));
        if (index < 0)
        {
            index = ~index;
            // 如果在跨经中找不到, 那么第一个元素应该返回( 我们在环上)
            if (index == _ring.Length) index = 0;
        }
        return _ring[index];
    }

    private readonly struct Searcher : IComparable<QueueId>
    {
        private readonly uint _value;
        public Searcher(uint value) => _value = value;
        public int CompareTo(QueueId other) => _value.CompareTo(other.GetUniformHashCode());
    }

    public override string ToString()
        => $"All QueueIds:{Environment.NewLine}{(Utils.EnumerableToString(_ring, elem => $"{elem}/x{elem.GetUniformHashCode():X8}", Environment.NewLine, false))}";
}
