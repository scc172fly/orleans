using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="ImmutableSortedDictionary{TKey, TValue}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterSerializer]
    public sealed class ImmutableSortedDictionaryCodec<TKey, TValue> : GeneralizedReferenceTypeSurrogateCodec<ImmutableSortedDictionary<TKey, TValue>, ImmutableSortedDictionarySurrogate<TKey, TValue>> where TKey : notnull
    {
        /// <summary>
        /// 初始化 <see cref="ImmutableSortedDictionaryCodec{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
        public ImmutableSortedDictionaryCodec(IValueSerializer<ImmutableSortedDictionarySurrogate<TKey, TValue>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override ImmutableSortedDictionary<TKey, TValue> ConvertFromSurrogate(ref ImmutableSortedDictionarySurrogate<TKey, TValue> surrogate)
            => ImmutableSortedDictionary.CreateRange(surrogate.KeyComparer, surrogate.ValueComparer, surrogate.Values);

        /// <inheritdoc/>
        public override void ConvertToSurrogate(ImmutableSortedDictionary<TKey, TValue> value, ref ImmutableSortedDictionarySurrogate<TKey, TValue> surrogate)
        {
            surrogate.Values = new(value);
            surrogate.KeyComparer = value.KeyComparer != Comparer<TKey>.Default ? value.KeyComparer : null;
            surrogate.ValueComparer = value.ValueComparer != EqualityComparer<TValue>.Default ? value.ValueComparer : null;
        }
    }

    /// <summary>
    /// <see cref="ImmutableSortedDictionaryCodec{TKey, TValue}"/> 所使用的代理类型。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [GenerateSerializer]
    public struct ImmutableSortedDictionarySurrogate<TKey, TValue> where TKey : notnull
    {
        /// <summary>
        /// 获取或设置值集合。
        /// </summary>
        /// <value>值集合。</value>
        [Id(0)]
        public List<KeyValuePair<TKey, TValue>> Values;

        /// <summary>
        /// 获取或设置键比较器。
        /// </summary>
        /// <value>键比较器。</value>
        [Id(1)]
        public IComparer<TKey>? KeyComparer;

        /// <summary>
        /// 获取或设置值比较器。
        /// </summary>
        [Id(2)]
        public IEqualityComparer<TValue>? ValueComparer;
    }

    /// <summary>
    /// <see cref="ImmutableSortedDictionary{TKey, TValue}"/> 的复制器。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterCopier]
    public sealed class ImmutableSortedDictionaryCopier<TKey, TValue> : IDeepCopier<ImmutableSortedDictionary<TKey, TValue>>, IOptionalDeepCopier where TKey : notnull
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<TKey> _keyCopier;
        private readonly IDeepCopier<TValue> _valueCopier;

        /// <summary>
        /// 初始化 <see cref="ImmutableSortedDictionaryCopier{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="keyCopier">键复制器。</param>
        /// <param name="valueCopier">值复制器。</param>
        public ImmutableSortedDictionaryCopier(IDeepCopier<TKey> keyCopier, IDeepCopier<TValue> valueCopier)
        {
            _keyCopier = OrleansGeneratedCodeHelper.UnwrapService(this, keyCopier);
            _valueCopier = OrleansGeneratedCodeHelper.UnwrapService(this, valueCopier);
        }

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._keyCopier) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._valueCopier));

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public ImmutableSortedDictionary<TKey, TValue>? DeepCopy(ImmutableSortedDictionary<TKey, TValue>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<ImmutableSortedDictionary<TKey, TValue>>(input, out var result))
                return result!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.IsEmpty || IsShallowCopyable())
                return input;

            // 如果输入集合中有任何值能够参与循环引用，则此处可能发生无限递归。
            // 在这种情况下返回浅拷贝，以缓解该问题。
            context.RecordCopy(input, input);

            var shallowKeys = OrleansGeneratedCodeHelper.IsShallowCopyable(_keyCopier);
            var shallowValues = OrleansGeneratedCodeHelper.IsShallowCopyable(_valueCopier);
            var items = new List<KeyValuePair<TKey, TValue>>(input.Count);
            foreach (var item in input)
                items.Add(new(shallowKeys ? item.Key : _keyCopier.DeepCopy(item.Key, context),
                    shallowValues ? item.Value : _valueCopier.DeepCopy(item.Value, context)!));

            var res = ImmutableSortedDictionary.CreateRange(input.KeyComparer, input.ValueComparer, items);
            context.RecordCopy(input, res);
            return res;
        }
    }
}
