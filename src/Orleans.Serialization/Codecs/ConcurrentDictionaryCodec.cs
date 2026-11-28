using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="ConcurrentDictionary{TKey, TValue}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterSerializer]
    public sealed class ConcurrentDictionaryCodec<TKey, TValue> : GeneralizedReferenceTypeSurrogateCodec<ConcurrentDictionary<TKey, TValue>, ConcurrentDictionarySurrogate<TKey, TValue>> where TKey : notnull
    {
        /// <summary>
        /// 初始化 <see cref="ConcurrentDictionaryCodec{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
        public ConcurrentDictionaryCodec(IValueSerializer<ConcurrentDictionarySurrogate<TKey, TValue>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override ConcurrentDictionary<TKey, TValue> ConvertFromSurrogate(ref ConcurrentDictionarySurrogate<TKey, TValue> surrogate)
            => new(surrogate.Values, surrogate.Values.Comparer);

        /// <inheritdoc/>
        public override void ConvertToSurrogate(ConcurrentDictionary<TKey, TValue> value, ref ConcurrentDictionarySurrogate<TKey, TValue> surrogate)
#if NET6_0_OR_GREATER
            => surrogate.Values = new(value, value.Comparer);
#else
            => surrogate.Values = new(value);
#endif
    }

    /// <summary>
    /// <see cref="ConcurrentDictionaryCodec{TKey, TValue}"/> 所使用的代理类型。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [GenerateSerializer]
    public struct ConcurrentDictionarySurrogate<TKey, TValue> where TKey : notnull
    {
        /// <summary>
        /// 获取或设置值集合。
        /// </summary>
        /// <value>值集合。</value>
        [Id(0)]
        public Dictionary<TKey, TValue> Values;
    }

    /// <summary>
    /// <see cref="ConcurrentDictionary{TKey, TValue}"/> 的复制器。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterCopier]
    public sealed class ConcurrentDictionaryCopier<TKey, TValue> : IDeepCopier<ConcurrentDictionary<TKey, TValue>>, IBaseCopier<ConcurrentDictionary<TKey, TValue>> where TKey : notnull
    {
        private readonly Type _fieldType = typeof(ConcurrentDictionary<TKey, TValue>);
        private readonly IDeepCopier<TKey> _keyCopier;
        private readonly IDeepCopier<TValue> _valueCopier;

        /// <summary>
        /// 初始化 <see cref="ConcurrentDictionaryCopier{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="keyCopier">键复制器。</param>
        /// <param name="valueCopier">值复制器。</param>
        public ConcurrentDictionaryCopier(IDeepCopier<TKey> keyCopier, IDeepCopier<TValue> valueCopier)
        {
            _keyCopier = keyCopier;
            _valueCopier = valueCopier;
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public ConcurrentDictionary<TKey, TValue>? DeepCopy(ConcurrentDictionary<TKey, TValue>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<ConcurrentDictionary<TKey, TValue>>(input, out var result))
            {
                return result!;
            }

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
            {
                return context.DeepCopy(input);
            }

#if NET6_0_OR_GREATER
            result = new(input.Comparer);
#else
            result = new();
#endif
            context.RecordCopy(input, result);
            foreach (var pair in input)
            {
                result[_keyCopier.DeepCopy(pair.Key, context)!] = _valueCopier.DeepCopy(pair.Value, context)!;
            }

            return result;
        }

        /// <inheritdoc/>
        public void DeepCopy(ConcurrentDictionary<TKey, TValue> input, ConcurrentDictionary<TKey, TValue> output, CopyContext context)
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
