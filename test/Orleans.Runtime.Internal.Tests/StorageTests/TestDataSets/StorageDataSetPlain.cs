using System.Collections;
using Orleans.Runtime;

namespace UnitTests.StorageTests.Relational.TestDataSets
{
    /// <summary>
    /// 一套简单的测试数据集,机智且不使用扩展键。
    /// </summary>
    /// <typeparam name="TGrainKey"> grain 类型(整数、信头或字符串)</typeparam>。
    internal sealed class StorageDataSetPlain<TGrainKey> : IEnumerable<object[]>
    {
        private const int NumCases = 3;

        /// <summary>
        /// 符号设置此数据集的使用 。
        /// </summary>
        private static SymbolSet Symbols { get; } = new SymbolSet(SymbolSet.Latin1);

        /// <summary>
        /// 随机抽取的字符串长度窗体 <see cref="Symbols"/>。
        /// </summary>
        private const long StringLength = 15L;

        public static (string, GrainId, GrainState<TestState1>) GetTestData(int testNum) => testNum switch
        {
            0 => (
                GrainTypeGenerator.GetGrainType<TGrainKey>(),
                RandomUtilities.GetRandomGrainId<TGrainKey>(),
                new GrainState<TestState1> { State = new TestState1 { A = RandomUtilities.GetRandomCharacters(Symbols, StringLength), B = 1, C = 4 } }),
            1 => (
                GrainTypeGenerator.GetGrainType<TGrainKey>(),
                RandomUtilities.GetRandomGrainId<TGrainKey>(true),
                new GrainState<TestState1> { State = new TestState1 { A = RandomUtilities.GetRandomCharacters(Symbols, StringLength), B = 2, C = 5 } }),
            2 => (
                GrainTypeGenerator.GetGrainType<TGrainKey>(),
                RandomUtilities.GetRandomGrainId<TGrainKey>(true),
                new GrainState<TestState1> { State = new TestState1 { A = RandomUtilities.GetRandomCharacters(Symbols, StringLength), B = 3, C = 6 } }),
            _ => throw new IndexOutOfRangeException(),

        };

        public IEnumerator<object[]> GetEnumerator() => Enumerable.Range(0, NumCases).Select(n => new object[] { n }).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
