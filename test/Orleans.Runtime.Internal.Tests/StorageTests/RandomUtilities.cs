using System.Text;
using Orleans.Runtime;

namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 工具功能为某些测试提供随机值。
    /// </summary>
    public static class RandomUtilities
    {
        /// <summary>
        /// 此型号代码与 Orleans.Runtime.Category.Grain =3一致, 也使用类似
        /// “公共类别 IdCategory {获得{返回 GetCategory(TypeCodeData);}}”
        /// 请注意,0L也有可能这样做。
        /// </summary>
        public const long NormalGrainTypeCode = 3L;

        /// <summary>
        /// 此型号代码与 Orleans.Runtime.Category.Grain =6一致, 也使用类似
        /// “公共类别 IdCategory {获得{返回 GetCategory(TypeCodeData);}}”
        /// 请注意,0L也有可能这样做。
        /// </summary>
        public const long KeyExtensionGrainTypeCode = 6L;

        /// <summary>
        /// 随机生成器列表。 此列表在构建后只读取 。
        /// </summary>
        /// <remarks>状态, 基本上 <see cref="Range{T}"/> 对象目前没有使用。</remarks>
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
        /// 获取给定类型的一个随机值 。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>给定类型随机值。</returns>
        /// <exception cref="ArgumentException"/>.
        public static T GetRandom<T>(Range<long>? range = null)
        {
            object? randomGenerator;
            if (RandomGenerators.TryGetValue(typeof(T), out randomGenerator))
            {
                //如果此字符串类型为某个符号, 则从中设定一个符号, 用于绘制给定的符号
                //和一种特殊参数。
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
        /// 随机获取符号。
        /// </summary>
        /// <param name="symbolSet">随机字符的任意字符所来自的符号集。</param>
        /// <param name="count">随机符号的计数。</param>
        /// <returns>一个随机字符串。</returns>
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
        /// 随机获取 grain ID。
        /// </summary>
        /// <typeparam name="TGrainKey"> grain 密钥类型。</typeparam>
        /// <typeparam name="TGrainGeneric">一种 grain.</typeparam>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="symbolSet">要使用的符号集。</param>
        /// 从符号集取的符号<param name="symbolCount">编号。</param>
        /// <returns>给定类型随机值。</returns>
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
        /// 随机获取 grain ID。
        /// </summary>
        /// <param name="keyExtension"> grain 扩展密钥。</param>
        /// 
        /// <returns>给定类型随机值。</returns>
        /// <exception cref="ArgumentException"/>.
        internal static GrainId GetRandomGrainId<T>(bool keyExtension = false)
        {
            //如果此字符串类型为某个符号, 则从中设定一个符号, 用于绘制给定的符号
            //和一种特殊参数。
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
