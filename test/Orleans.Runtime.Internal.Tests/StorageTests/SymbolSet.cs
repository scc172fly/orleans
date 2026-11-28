namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 表示一组字符编码范围。
    /// </summary>
    /// <remarks>包含用于构造字符编码的若干符号集合。更多信息参见 https://en.wikipedia.org/wiki/Character_encoding。</remarks>
    public class SymbolSet
    {
        /// <summary>
        /// 仅包含字母字符的 Latin-1（无标点符号、数字等）。
        /// </summary>
        public static IList<Range<int>> Latin1 { get; } = new List<Range<int>>(new[]
        {
            new Range<int>(0x0041, 0x005A),
            new Range<int>(0x0061, 0x007A),
            new Range<int>(0x00C0, 0x00D6),
            new Range<int>(0x00D8, 0x00F6),
            new Range<int>(0x00F8, 0x00FF),
        });

        /// <summary>
        /// 西里尔字符集。
        /// </summary>
        public static IList<Range<int>> Cyrillic { get; } = new List<Range<int>>(new[] { new Range<int>(0x0400, 0x04FF) });

        /// <summary>
        /// 希伯来字符集。
        /// </summary>
        public static IList<Range<int>> Hebrew { get; } = new List<Range<int>>(new[] { new Range<int>(0x05D0, 0x05EA) });

        /// <summary>
        /// Unicode 表情符号。
        /// </summary>
        public static IList<Range<int>> Emoticons { get; } = new List<Range<int>>(new[] { new Range<int>(0x1F600, 0x1F64F) });

        /// <summary>
        /// Dingbats 装饰符号字符。
        /// </summary>
        public static IList<Range<int>> Dingbats { get; } = new List<Range<int>>(new[] { new Range<int>(0x2700, 0x27BF) });

        /// <summary>
        /// 由此集合定义的符号集合。
        /// </summary>
        public IList<Range<int>> SetRanges { get; }

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="setRanges">用于收集此集合中符号的一组符号范围。</param>
        public SymbolSet(IEnumerable<Range<int>> setRanges)
        {
            if (setRanges == null)
            {
                throw new ArgumentNullException(nameof(setRanges));
            }

            SetRanges = new List<Range<int>>(setRanges);
        }
    }
}
