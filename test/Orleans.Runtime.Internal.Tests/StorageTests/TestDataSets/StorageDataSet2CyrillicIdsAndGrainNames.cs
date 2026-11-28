using System.Collections;
using Orleans.Runtime;

namespace UnitTests.StorageTests.Relational.TestDataSets
{
    /// <summary>
    /// 用于具有西里尔字母名称和 ID 的 Grain 的数据集。
    /// </summary>
    /// <typeparam name="TStateData"><see cref="TestStateGeneric1{T}"/> 的类型。</typeparam>
    internal class StorageDataSet2CyrillicIdsAndGrainNames<TStateData> : IEnumerable<object[]>
    {
        private const int NumCases = 3;

        /// <summary>
        /// 此数据集使用的符号集。
        /// </summary>
        private static SymbolSet Symbols { get; } = new SymbolSet(SymbolSet.Cyrillic);

        /// <summary>
        /// 从 <see cref="Symbols"/> 中抽取的随机字符串长度。
        /// </summary>
        private const long StringLength = 15L;

        public static (string, GrainId, GrainState<TestStateGeneric1<TStateData>>) GetTestData(int testNum) => testNum switch
        {
            0 => (
                GrainTypeGenerator.GetGrainType<string>(),
                RandomUtilities.GetRandomGrainId<string, int>(Symbols, StringLength),
                new GrainState<TestStateGeneric1<TStateData>>
                {
                    State = new TestStateGeneric1<TStateData>
                    {
                        SomeData = RandomUtilities.GetRandom<TStateData>(),
                        A = "Data1",
                        B = 1,
                        C = 4
                    }
                }),
            1 => (
                GrainTypeGenerator.GetGrainType<string>(),
                RandomUtilities.GetRandomGrainId<string, int>(Symbols, StringLength),
                new GrainState<TestStateGeneric1<TStateData>>
                {
                    State = new TestStateGeneric1<TStateData>
                    {
                        SomeData = RandomUtilities.GetRandom<TStateData>(),
                        A = "Data2",
                        B = 2,
                        C = 5
                    }
                }),
            2 => (
                GrainTypeGenerator.GetGrainType<string>(),
                RandomUtilities.GetRandomGrainId<string, int>(Symbols, StringLength),
                new GrainState<TestStateGeneric1<TStateData>>
                {
                    State = new TestStateGeneric1<TStateData>
                    {
                        SomeData = RandomUtilities.GetRandom<TStateData>(),
                        A = "Data3",
                        B = 3,
                        C = 6
                    }
                }),
            _ => throw new IndexOutOfRangeException()
        };

        public IEnumerator<object[]> GetEnumerator() => Enumerable.Range(0, NumCases).Select(n => new object[] { n }).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
