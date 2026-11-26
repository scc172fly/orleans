namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 装饰一套字符编码范围
    /// </summary>
    /// <remarks>包含用于创建字符编码的一些符号组。请参见 https://en.wikipedia.org/wiki/Character_encoding中的更多符号组。</remarks>
    public class SymbolSet
    {
        /// <summary>
        /// 拉丁语-1 仅带有字符( 无标点、 数字等) 。
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
        /// 西里尔字母组
        /// </summary>
        public static IList<Range<int>> Cyrillic { get; } = new List<Range<int>>(new[] { new Range<int>(0x0400, 0x04FF) });

        /// <summary>
        /// 希伯来文字符集。
        /// </summary>
        public static IList<Range<int>> Hebrew { get; } = new List<Range<int>>(new[] { new Range<int>(0x05D0, 0x05EA) });

        /// <summary>
        /// 统一密码表情
        /// </summary>
        public static IList<Range<int>> Emoticons { get; } = new List<Range<int>>(new[] { new Range<int>(0x1F600, 0x1F64F) });

        /// <summary>
        /// 丁巴人角色。
        /// </summary>
        public static IList<Range<int>> Dingbats { get; } = new List<Range<int>>(new[] { new Range<int>(0x2700, 0x27BF) });

        /// <summary>
        /// 由此集定义的符号集 。
        /// </summary>
        public IList<Range<int>> SetRanges { get; }

        /// <summary>
        /// 建筑师
        /// </summary>
        /// <param name="setRanges">一组符号区域,用于收集此收藏中的符号。</param>
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
