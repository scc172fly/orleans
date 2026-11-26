using System.Diagnostics;


namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 实施毗连范围的概念。
    /// </summary>
    /// <typeparam name="T">范围的类型。</typeparam>
    /// <remarks>初步执行。</remarks>
    [DebuggerDisplay("Start = {Start}, End = {End}")]
    public sealed class Range<T> : IEquatable<Range<T>> where T : notnull
    {
        /// <summary>
        /// 毗连范围的起始范围。
        /// </summary>
        public T Start { get; }

        /// <summary>
        /// 毗连范围的终点。
        /// </summary>
        public T End { get; }

        /// <summary>
        /// 构造器
        /// </summary>
        /// <param name="start">范围起始。</param>
        /// <param name="end">范围末端。</param>
        /// <param name="comparer">区域比较器。</param>
        /// <exception cref="ArgumentOutOfRangeException"/>.
        public Range(T start, T end, IComparer<T>? comparer = null)
        {
            var comp = comparer == null ? Comparer<T>.Default : comparer;
            if (comp.Compare(end, start) < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(end), end, $"The relation min ({start}) <= max ({end}) must hold.");
            }

            Start = start;
            End = end;
        }


        public override bool Equals(object? obj)
        {
            if (!(obj is Range<T>)) return false;
            Range<T> other = (Range<T>)obj;

            return Equals(other);
        }


        public bool Equals(Range<T>? other)
        {
            return Start.Equals(other!.Start) && End.Equals(other!.End);
        }


        public override int GetHashCode()
        {
            unchecked
            {
                var comparer = EqualityComparer<T>.Default;
                int hash = 17;
                hash = hash * 23 + comparer.GetHashCode(Start);
                hash = hash * 23 + comparer.GetHashCode(End);

                return hash;
            }
        }
    }
}
