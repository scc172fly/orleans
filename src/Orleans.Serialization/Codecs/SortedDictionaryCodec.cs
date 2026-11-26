using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using System;
using System.Collections.Generic;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="SortedDictionary{TKey, TValue}"/>的序列器。
    /// </summary>
    /// <typeparam name="TKey">密钥类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterSerializer]
    public sealed class SortedDictionaryCodec<TKey, TValue> : GeneralizedReferenceTypeSurrogateCodec<SortedDictionary<TKey, TValue>, SortedDictionarySurrogate<TKey, TValue>> where TKey : notnull
    {
        /// <summary>
        /// 初始化 <see cref="SortedDictionaryCodec{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代孕序列器</param>
        public SortedDictionaryCodec(IValueSerializer<SortedDictionarySurrogate<TKey, TValue>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc />
        public override SortedDictionary<TKey, TValue> ConvertFromSurrogate(ref SortedDictionarySurrogate<TKey, TValue> surrogate)
        {
            var result = new SortedDictionary<TKey, TValue>(surrogate.Comparer);
            foreach (var kvp in surrogate.Values)
            {
                result.Add(kvp.Key, kvp.Value);
            }

            return result;
        }

        /// <inheritdoc />
        public override void ConvertToSurrogate(SortedDictionary<TKey, TValue> value, ref SortedDictionarySurrogate<TKey, TValue> surrogate)
        {
            surrogate.Values = new(value);
            surrogate.Comparer = value.Comparer == Comparer<TKey>.Default ? null : value.Comparer;
        }
    }

    /// <summary>
    /// <see cref="SortedDictionaryCodec{TKey, TValue}"/>的代用类型。
    /// </summary>
    /// <typeparam name="TKey">密钥类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [GenerateSerializer]
    public struct SortedDictionarySurrogate<TKey, TValue> where TKey : notnull
    {
        /// <summary>
        /// 获取或设置值。
        /// </summary>
        /// <value>价值。</value>
        [Id(0)]
        public List<KeyValuePair<TKey, TValue>> Values;

        /// <summary>
        /// 设置比较器或获取比对器。
        /// </summary>
        /// <value>比较器。</value>
        [Id(1)]
        public IComparer<TKey>? Comparer;
    }

    /// <summary>
    /// <see cref="SortedDictionary{TKey, TValue}"/>的复印机。
    /// </summary>
    /// <typeparam name="TKey">密钥类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterCopier]
    public sealed class SortedDictionaryCopier<TKey, TValue> : IDeepCopier<SortedDictionary<TKey, TValue>>, IBaseCopier<SortedDictionary<TKey, TValue>> where TKey : notnull
    {
        private readonly Type _fieldType = typeof(SortedDictionary<TKey, TValue>);
        private readonly IDeepCopier<TKey> _keyCopier;
        private readonly IDeepCopier<TValue> _valueCopier;

        /// <summary>
        /// 初始化 <see cref="SortedDictionaryCopier{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="keyCopier">钥匙复印机。</param>
        /// <param name="valueCopier">价值复印机。</param>
        public SortedDictionaryCopier(IDeepCopier<TKey> keyCopier, IDeepCopier<TValue> valueCopier)
        {
            _keyCopier = keyCopier;
            _valueCopier = valueCopier;
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public SortedDictionary<TKey, TValue>? DeepCopy(SortedDictionary<TKey, TValue>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<SortedDictionary<TKey, TValue>>(input, out var result))
            {
                return result!;
            }

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
            {
                return context.DeepCopy(input);
            }

            result = new SortedDictionary<TKey, TValue>(input.Comparer);
            context.RecordCopy(input, result);
            foreach (var pair in input)
            {
                result[_keyCopier.DeepCopy(pair.Key, context)!] = _valueCopier.DeepCopy(pair.Value, context)!;
            }

            return result;
        }

        /// <inheritdoc />
        public void DeepCopy(SortedDictionary<TKey, TValue> input, SortedDictionary<TKey, TValue> output, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(input);
            ArgumentNullExceptionPolyfill.ThrowIfNull(output);
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            foreach (var pair in input)
            {
                output[_keyCopier.DeepCopy(pair.Key, context)!] = _valueCopier.DeepCopy(pair.Value, context)!;
            }
        }
    }
}
