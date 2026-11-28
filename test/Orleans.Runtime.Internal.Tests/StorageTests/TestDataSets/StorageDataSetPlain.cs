using System.Collections;
using Orleans.Runtime;

namespace UnitTests.StorageTests.Relational.TestDataSets
{
    /// <summary>
    /// 一组包含有扩展键和无扩展键两种情况的简单测试数据集。
    /// </summary>
    /// <typeparam name="TGrainKey">Grain 类型（整数、GUID 或字符串）</typeparam>。
    internal sealed class StorageDataSetPlain<TGrainKey> : IEnumerable<object[]>
    {
        private const int NumCases = 3;

        /// <summary>
        /// 此数据集使用的符号集。
        /// </summary>
        private static SymbolSet Symbols { get; } = new SymbolSet(SymbolSet.Latin1);

        /// <summary>
        /// 从 <see cref="Symbols"/> 中抽取的随机字符串长度。
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
