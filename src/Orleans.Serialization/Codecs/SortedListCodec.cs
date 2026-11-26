using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using System;
using System.Collections.Generic;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="SortedList{TKey, TValue}"/>的序列器。
    /// </summary>
    /// <typeparam name="TKey">密钥类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterSerializer]
    public sealed class SortedListCodec<TKey, TValue> : GeneralizedReferenceTypeSurrogateCodec<SortedList<TKey, TValue>, SortedListSurrogate<TKey, TValue>> where TKey : notnull
    {
        /// <summary>
        /// 初始化 <see cref="SortedListCodec{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代孕序列器</param>
        public SortedListCodec(IValueSerializer<SortedListSurrogate<TKey, TValue>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc />
        public override SortedList<TKey, TValue>? ConvertFromSurrogate(ref SortedListSurrogate<TKey, TValue> surrogate)
        {
            if (surrogate.Values is null)
            {
                return null;
            }
            else
            {
                var values = surrogate.Values!;
                var result = new SortedList<TKey, TValue>(values.Count, surrogate.Comparer);
                foreach (var kvp in values)
                {
                    result.Add(kvp.Key, kvp.Value);
                }

                return result;
            }
        }

        /// <inheritdoc />
        public override void ConvertToSurrogate(SortedList<TKey, TValue> value, ref SortedListSurrogate<TKey, TValue> surrogate)
        {
            surrogate.Values = new(value);
            surrogate.Comparer = value.Comparer == Comparer<TKey>.Default ? null : value.Comparer;
        }
    }

    /// <summary>
    /// <see cref="SortedListCodec{TKey, TValue}"/>的代用类型。
    /// </summary>
    /// <typeparam name="TKey">密钥类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [GenerateSerializer]
    public struct SortedListSurrogate<TKey, TValue> where TKey : notnull
    {
        /// <summary>
        /// 获取或设置值。
        /// </summary>
        /// <value>价值。</value>
        [Id(0)]
        public List<KeyValuePair<TKey, TValue>>? Values;

        /// <summary>
        /// 设置比较器或获取比对器。
        /// </summary>
        /// <value>比较器。</value>
        [Id(1)]
        public IComparer<TKey>? Comparer;
    }

    /// <summary>
    /// <see cref="SortedList{TKey, TValue}"/>的复印机。
    /// </summary>
    /// <typeparam name="TKey">密钥类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterCopier]
    public sealed class SortedListCopier<TKey, TValue> : IDeepCopier<SortedList<TKey, TValue>>, IBaseCopier<SortedList<TKey, TValue>> where TKey : notnull
    {
        private readonly Type _fieldType = typeof(SortedList<TKey, TValue>);
        private readonly IDeepCopier<TKey> _keyCopier;
        private readonly IDeepCopier<TValue> _valueCopier;

        /// <summary>
        /// 初始化 <see cref="SortedListCopier{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="keyCopier">钥匙复印机。</param>
        /// <param name="valueCopier">价值复印机。</param>
        public SortedListCopier(IDeepCopier<TKey> keyCopier, IDeepCopier<TValue> valueCopier)
        {
            _keyCopier = keyCopier;
            _valueCopier = valueCopier;
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public SortedList<TKey, TValue>? DeepCopy(SortedList<TKey, TValue>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<SortedList<TKey, TValue>>(input, out var result))
            {
                return result!;
            }

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
            {
                return context.DeepCopy(input);
            }

            result = new SortedList<TKey, TValue>(input.Comparer);
            context.RecordCopy(input, result);
            foreach (var pair in input)
            {
                result[_keyCopier.DeepCopy(pair.Key, context)!] = _valueCopier.DeepCopy(pair.Value, context)!;
            }

            return result;
        }

        /// <inheritdoc />
        public void DeepCopy(SortedList<TKey, TValue> input, SortedList<TKey, TValue> output, CopyContext context)
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
