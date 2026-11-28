#if NET8_0_OR_GREATER
using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="FrozenDictionary{TKey, TValue}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterSerializer]
    public sealed class FrozenDictionaryCodec<TKey, TValue> : GeneralizedReferenceTypeSurrogateCodec<FrozenDictionary<TKey, TValue>, FrozenDictionarySurrogate<TKey, TValue>> where TKey : notnull
    {
        /// <summary>
        /// 初始化 <see cref="FrozenDictionaryCodec{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
        public FrozenDictionaryCodec(IValueSerializer<FrozenDictionarySurrogate<TKey, TValue>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override FrozenDictionary<TKey, TValue> ConvertFromSurrogate(ref FrozenDictionarySurrogate<TKey, TValue> surrogate)
            => surrogate.Values.ToFrozenDictionary(surrogate.KeyComparer);

        /// <inheritdoc/>
        public override void ConvertToSurrogate(FrozenDictionary<TKey, TValue> value, ref FrozenDictionarySurrogate<TKey, TValue> surrogate)
        {
            surrogate.Values = new(value);
            surrogate.KeyComparer = value.Comparer != EqualityComparer<TKey>.Default ? value.Comparer : null;
        }
    }

    /// <summary>
    /// <see cref="FrozenDictionaryCodec{TKey, TValue}"/> 所使用的代理类型。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [GenerateSerializer]
    public struct FrozenDictionarySurrogate<TKey, TValue> where TKey : notnull
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
        public IEqualityComparer<TKey>? KeyComparer;
    }

    /// <summary>
    /// <see cref="FrozenDictionary{TKey, TValue}"/> 的复制器。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterCopier]
    public sealed class FrozenDictionaryCopier<TKey, TValue> : IDeepCopier<FrozenDictionary<TKey, TValue>>, IOptionalDeepCopier, IDerivedTypeCopier where TKey : notnull
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<TKey> _keyCopier;
        private readonly IDeepCopier<TValue> _valueCopier;

        /// <summary>
        /// 初始化 <see cref="FrozenDictionaryCopier{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="keyCopier">键复制器。</param>
        /// <param name="valueCopier">值复制器。</param>
        public FrozenDictionaryCopier(IDeepCopier<TKey> keyCopier, IDeepCopier<TValue> valueCopier)
        {
            _keyCopier = OrleansGeneratedCodeHelper.UnwrapService(this, keyCopier);
            _valueCopier = OrleansGeneratedCodeHelper.UnwrapService(this, valueCopier);
        }

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._keyCopier) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._valueCopier));

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public FrozenDictionary<TKey, TValue>? DeepCopy(FrozenDictionary<TKey, TValue>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<FrozenDictionary<TKey, TValue>>(input, out var result))
                return result!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.Count == 0 || IsShallowCopyable())
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

            var res = items.ToFrozenDictionary(input.Comparer);
            context.RecordCopy(input, res);
            return res;
        }
    }
}
#endif
