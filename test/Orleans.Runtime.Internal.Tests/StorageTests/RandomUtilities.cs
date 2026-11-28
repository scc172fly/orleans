using System.Text;
using Orleans.Runtime;

namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 为部分测试提供随机值的辅助函数。
    /// </summary>
    public static class RandomUtilities
    {
        /// <summary>
        /// 此类型代码与 Orleans.Runtime.Category.Grain = 3 一致，也用于如下形式：
        /// "public Category IdCategory { get { return GetCategory(TypeCodeData); } }".
        /// 注意，0L 也可能适用。
        /// </summary>
        public const long NormalGrainTypeCode = 3L;

        /// <summary>
        /// 此类型代码与 Orleans.Runtime.Category.Grain = 6 一致，也用于如下形式：
        /// "public Category IdCategory { get { return GetCategory(TypeCodeData); } }".
        /// 注意，0L 也可能适用。
        /// </summary>
        public const long KeyExtensionGrainTypeCode = 6L;

        /// <summary>
        /// 正在使用的随机生成器列表。此列表在构造后为只读。
        /// </summary>
        /// <remarks>目前未使用状态，实质上是 <see cref="Range{T}"/> 对象。</remarks>
        private static Dictionary<Type, object> RandomGenerators { get; } = new Dictionary<Type, object>
        {
            [typeof(Guid)] = new Func<object?, Guid>(_ => Guid.NewGuid()),
            [typeof(int)] = new Func<object?, int>(_ => Random.Shared.Next()),
            [typeof(long)] = new Func<object?, long>(_ => Random.Shared.NextInt64()),
            [typeof(string)] = new Func<object?, string>(symbolSet =>
            {
                var count = ((Tuple<Range<long>, SymbolSet>)symbolSet!).Item1.Start;
                var symbols = ((Tuple<Range<long>, SymbolSet>)symbolSet).Item2;
                var builder = new StringBuilder();
                for (long i = 0; i < count; ++i)
                {
                    var symbolRange = symbols.SetRanges[Random.Shared.Next(symbols.SetRanges.Count)];
                    builder.Append((char)Random.Shared.Next(symbolRange.Start, symbolRange.End));
                }

                return builder.ToString();
            })
        };

        /// <summary>
        /// 获取指定类型的随机值。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>指定类型的随机值。</returns>
        /// <exception cref="ArgumentException"/>.
        public static T GetRandom<T>(Range<long>? range = null)
        {
            object? randomGenerator;
            if (RandomGenerators.TryGetValue(typeof(T), out randomGenerator))
            {
                //若为字符串类型，则需要提供用于选取字符的符号集，
                //并构造一种特殊的参数。
                if ((typeof(T) == typeof(string)))
                {
                    const long SymbolsDefaultCount = 15;
                    var symbols = new SymbolSet(SymbolSet.Latin1);
                    return ((Func<object?, T>)randomGenerator)(Tuple.Create(range ?? new Range<long>(SymbolsDefaultCount, SymbolsDefaultCount), symbols));
                }

                return ((Func<object?, T>)randomGenerator)(null);
            }

            throw new ArgumentException(typeof(T).Name);
        }

        /// <summary>
        /// 获取随机符号。
        /// </summary>
        /// <param name="symbolSet">用于随机选取字符的符号集。</param>
        /// <param name="count">随机符号的数量。</param>
        /// <returns>随机字符串。</returns>
        /// <exception cref="ArgumentNullException"/>.
        /// <exception cref="ArgumentOutOfRangeException"/>.
        public static string GetRandomCharacters(SymbolSet symbolSet, long count)
        {
            if (symbolSet == null)
            {
                throw new ArgumentNullException(nameof(symbolSet));
            }

            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "The count news to be more than zero.");
            }

            object randomGenerator = RandomGenerators[typeof(string)];
            return ((Func<object, string>)randomGenerator)(Tuple.Create(new Range<long>(count, count), symbolSet));
        }

        /// <summary>
        /// 获取随机 Grain ID。
        /// </summary>
        /// <typeparam name="TGrainKey">Grain 键的类型。</typeparam>
        /// <typeparam name="TGrainGeneric">Grain 泛型部分的类型。</typeparam>
        /// <param name="grainFactory">Grain 工厂。（译注：此参数仅见于原注释，当前方法签名中不存在。）</param>
        /// <param name="symbolSet">要使用的符号集。</param>
        /// <param name="symbolCount">从符号集中选取的符号数量。</param>
        /// <returns>指定类型的随机值。（译注：实际返回使用指定键类型生成的 GrainId。）</returns>
        /// <exception cref="ArgumentException"/>.
        internal static GrainId GetRandomGrainId<TGrainKey, TGrainGeneric>(SymbolSet symbolSet, long symbolCount = 15)
        {
            if (symbolSet == null)
            {
                throw new ArgumentNullException(nameof(symbolSet));
            }

            if (typeof(TGrainKey) == typeof(string))
            {
                var grainId = GetRandomCharacters(symbolSet, symbolCount);
                return LegacyGrainId.FromParsableString(LegacyGrainId.GetGrainId(NormalGrainTypeCode, grainId).ToParsableString());
            }

            throw new ArgumentException(typeof(TGrainKey).Name);
        }

        /// <summary>
        /// 获取随机 Grain ID。
        /// </summary>
        /// <param name="keyExtension">Grain 扩展键。（译注：此布尔参数控制是否生成扩展键。）</param>
        /// 
        /// <returns>指定类型的随机值。（译注：实际返回使用指定键类型生成的 GrainId。）</returns>
        /// <exception cref="ArgumentException"/>.
        internal static GrainId GetRandomGrainId<T>(bool keyExtension = false)
        {
            //若为字符串类型，则需要提供用于选取字符的符号集，
            //并构造一种特殊的参数。
            const long SymbolsDefaultCount = 15;
            var symbolSet = new SymbolSet(SymbolSet.Latin1);

            if (typeof(T) == typeof(Guid))
            {
                var extension = keyExtension ? GetRandomCharacters(symbolSet, SymbolsDefaultCount) : null;
                return LegacyGrainId.GetGrainId(UniqueKey.NewKey(Guid.NewGuid(), keyExtension ? UniqueKey.Category.KeyExtGrain : UniqueKey.Category.Grain, keyExtension ? KeyExtensionGrainTypeCode : NormalGrainTypeCode, extension));
            }

            if (typeof(T) == typeof(long))
            {
                var extension = keyExtension ? GetRandomCharacters(symbolSet, SymbolsDefaultCount) : null;
                return LegacyGrainId.GetGrainId(UniqueKey.NewKey(Random.Shared.NextInt64(), keyExtension ? UniqueKey.Category.KeyExtGrain : UniqueKey.Category.Grain, keyExtension ? KeyExtensionGrainTypeCode : NormalGrainTypeCode, extension));
            }

            if (typeof(T) == typeof(string))
            {
                var grainId = GetRandomCharacters(symbolSet, SymbolsDefaultCount);
                return LegacyGrainId.GetGrainId(NormalGrainTypeCode, grainId);
            }

            throw new ArgumentException(typeof(T).Name);
        }
    }
}
