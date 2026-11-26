using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="ImmutableDictionary{TKey, TValue}"/>的序列器。
    /// </summary>
    /// <typeparam name="TKey">密钥类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterSerializer]
    public sealed class ImmutableDictionaryCodec<TKey, TValue> : GeneralizedReferenceTypeSurrogateCodec<ImmutableDictionary<TKey, TValue>, ImmutableDictionarySurrogate<TKey, TValue>> where TKey : notnull
    {
        /// <summary>
        /// 初始化 <see cref="ImmutableDictionaryCodec{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代孕序列器</param>
        public ImmutableDictionaryCodec(IValueSerializer<ImmutableDictionarySurrogate<TKey, TValue>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override ImmutableDictionary<TKey, TValue> ConvertFromSurrogate(ref ImmutableDictionarySurrogate<TKey, TValue> surrogate)
            => ImmutableDictionary.CreateRange(
                surrogate.Values.Comparer,
                surrogate.ValueComparer ?? EqualityComparer<TValue>.Default,
                surrogate.Values);

        /// <inheritdoc/>
        public override void ConvertToSurrogate(ImmutableDictionary<TKey, TValue> value, ref ImmutableDictionarySurrogate<TKey, TValue> surrogate)
        {
            surrogate.Values = new(value, value.KeyComparer);
            surrogate.ValueComparer = value.ValueComparer != EqualityComparer<TValue>.Default ? value.ValueComparer : null;
        }
    }

    /// <summary>
    /// <see cref="ImmutableDictionaryCodec{TKey, TValue}"/>所使用的代金矿类型。
    /// </summary>
    /// <typeparam name="TKey">密钥类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [GenerateSerializer]
    public struct ImmutableDictionarySurrogate<TKey, TValue> where TKey : notnull
    {
        /// <summary>
        /// 获取或设置值。
        /// </summary>
        /// <value>价值。</value>
        [Id(0)]
        public Dictionary<TKey, TValue> Values;

        /// <summary>
        /// 获取或设置数值比较器。
        /// </summary>
        [Id(1)]
        public IEqualityComparer<TValue>? ValueComparer;
    }

    /// <summary>
    /// <see cref="ImmutableDictionary{TKey, TValue}"/>的复印机。
    /// </summary>
    /// <typeparam name="TKey">密钥类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterCopier]
    public sealed class ImmutableDictionaryCopier<TKey, TValue> : IDeepCopier<ImmutableDictionary<TKey, TValue>>, IOptionalDeepCopier where TKey : notnull
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<TKey> _keyCopier;
        private readonly IDeepCopier<TValue> _valueCopier;

        /// <summary>
        /// 初始化 <see cref="ImmutableDictionaryCopier{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="keyCopier">钥匙复印机。</param>
        /// <param name="valueCopier">价值复印机。</param>
        public ImmutableDictionaryCopier(IDeepCopier<TKey> keyCopier, IDeepCopier<TValue> valueCopier)
        {
            _keyCopier = OrleansGeneratedCodeHelper.UnwrapService(this, keyCopier);
            _valueCopier = OrleansGeneratedCodeHelper.UnwrapService(this, valueCopier);
        }

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._keyCopier) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._valueCopier));

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public ImmutableDictionary<TKey, TValue>? DeepCopy(ImmutableDictionary<TKey, TValue>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<ImmutableDictionary<TKey, TValue>>(input, out var result))
                return result!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.IsEmpty || IsShallowCopyable())
                return input;

            // 如果输入收藏的任何值能够参与循环参考, 此处就有可能无限重复。
            // 将这种案件退回浅本,从而减轻这一负担。
            context.RecordCopy(input, input);

            var shallowKeys = OrleansGeneratedCodeHelper.IsShallowCopyable(_keyCopier);
            var shallowValues = OrleansGeneratedCodeHelper.IsShallowCopyable(_valueCopier);
            var items = new List<KeyValuePair<TKey, TValue>>(input.Count);
            foreach (var item in input)
                items.Add(new(shallowKeys ? item.Key : _keyCopier.DeepCopy(item.Key, context),
                    shallowValues ? item.Value : _valueCopier.DeepCopy(item.Value, context)!));

            var res = ImmutableDictionary.CreateRange(input.KeyComparer, input.ValueComparer, items);
            context.RecordCopy(input, res);
            return res;
        }
    }
}
