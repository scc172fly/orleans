using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="ReadOnlyDictionary{TKey, TValue}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterSerializer]
    public sealed class ReadOnlyDictionaryCodec<TKey, TValue> : GeneralizedReferenceTypeSurrogateCodec<ReadOnlyDictionary<TKey, TValue>, ReadOnlyDictionarySurrogate<TKey, TValue>> where TKey : notnull
    {
        /// <summary>
        /// 初始化 <see cref="ReadOnlyDictionaryCodec{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
        public ReadOnlyDictionaryCodec(IValueSerializer<ReadOnlyDictionarySurrogate<TKey, TValue>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc />
        public override ReadOnlyDictionary<TKey, TValue> ConvertFromSurrogate(ref ReadOnlyDictionarySurrogate<TKey, TValue> surrogate) => new(surrogate.Values);

        /// <inheritdoc />
        public override void ConvertToSurrogate(ReadOnlyDictionary<TKey, TValue> value, ref ReadOnlyDictionarySurrogate<TKey, TValue> surrogate) => surrogate.Values = new(value);
    }

    /// <summary>
    /// <see cref="ReadOnlyDictionary{TKey, TValue}"/> 的代理类型。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [GenerateSerializer]
    public struct ReadOnlyDictionarySurrogate<TKey, TValue> where TKey : notnull
    {
        /// <summary>
        /// 获取或设置字典条目。
        /// </summary>
        [Id(0)]
        public Dictionary<TKey, TValue> Values;
    }

    /// <summary>
    /// <see cref="ReadOnlyDictionary{TKey, TValue}"/> 的复制器。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterCopier]
    public sealed class ReadOnlyDictionaryCopier<TKey, TValue> : IDeepCopier<ReadOnlyDictionary<TKey, TValue>> where TKey : notnull
    {
        private readonly Type _fieldType = typeof(ReadOnlyDictionary<TKey, TValue>);
        private readonly IDeepCopier<TKey> _keyCopier;
        private readonly IDeepCopier<TValue> _valueCopier;

        /// <summary>
        /// 初始化 <see cref="ReadOnlyDictionaryCopier{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="keyCopier">键复制器。</param>
        /// <param name="valueCopier">值复制器。</param>
        public ReadOnlyDictionaryCopier(IDeepCopier<TKey> keyCopier, IDeepCopier<TValue> valueCopier)
        {
            _keyCopier = keyCopier;
            _valueCopier = valueCopier;
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public ReadOnlyDictionary<TKey, TValue>? DeepCopy(ReadOnlyDictionary<TKey, TValue>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<ReadOnlyDictionary<TKey, TValue>>(input, out var result))
            {
                return result!;
            }

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
            {
                return context.DeepCopy(input);
            }

            // 如果输入集合中有任何值能够参与循环引用，则此处可能发生无限递归。
            // 在这种情况下返回浅拷贝，以缓解该问题。
            context.RecordCopy(input, input);

            var temp = new Dictionary<TKey, TValue>(input.Count);
            foreach (var pair in input)
            {
                temp[_keyCopier.DeepCopy(pair.Key, context)!] = _valueCopier.DeepCopy(pair.Value, context)!;
            }

            result = new ReadOnlyDictionary<TKey, TValue>(temp);
            context.RecordCopy(input, result);
            return result;
        }
    }
}
