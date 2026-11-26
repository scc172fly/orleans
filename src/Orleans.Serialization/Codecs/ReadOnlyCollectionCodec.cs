using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="ReadOnlyCollectionCodec{T}"/>所使用的代金矿类型。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [GenerateSerializer]
    public struct ReadOnlyCollectionSurrogate<T>
    {
        /// <summary>
        /// 获取或设置值。
        /// </summary>
        /// <value>价值。</value>
        [Id(0)]
        public List<T> Values;
    }

    /// <summary>
    /// <see cref="ReadOnlyCollection{T}"/>的序列器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class ReadOnlyCollectionCodec<T> : GeneralizedReferenceTypeSurrogateCodec<ReadOnlyCollection<T>, ReadOnlyCollectionSurrogate<T>>
    {
        /// <summary>
        /// 初始化 <see cref="ReadOnlyCollectionCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代孕序列器</param>
        public ReadOnlyCollectionCodec(IValueSerializer<ReadOnlyCollectionSurrogate<T>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override ReadOnlyCollection<T> ConvertFromSurrogate(ref ReadOnlyCollectionSurrogate<T> surrogate) => new(surrogate.Values);

        /// <inheritdoc/>
        public override void ConvertToSurrogate(ReadOnlyCollection<T> value, ref ReadOnlyCollectionSurrogate<T> surrogate) => surrogate.Values = new(value);
    }

    /// <summary>
    /// <see cref="ReadOnlyCollection{T}"/>的复印机。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class ReadOnlyCollectionCopier<T> : IDeepCopier<ReadOnlyCollection<T>>
    {
        private readonly Type _fieldType = typeof(ReadOnlyCollection<T>);
        private readonly IDeepCopier<T> _elementCopier;

        /// <summary>
        /// 初始化 <see cref="ReadOnlyCollectionCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="elementCopier">元素复印机。</param>
        public ReadOnlyCollectionCopier(IDeepCopier<T> elementCopier)
        {
            _elementCopier = elementCopier;
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public ReadOnlyCollection<T>? DeepCopy(ReadOnlyCollection<T>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<ReadOnlyCollection<T>>(input, out var result))
            {
                return result!;
            }

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
            {
                return context.DeepCopy(input);
            }

            // 如果输入收藏的任何值能够参与循环参考, 此处就有可能无限重复。
            // 将这种案件退回浅本,从而减轻这一负担。
            context.RecordCopy(input, input);

            var tempResult = new T[input.Count];
            for (var i = 0; i < tempResult.Length; i++)
            {
                tempResult[i] = _elementCopier.DeepCopy(input[i], context)!;
            }

            result = new ReadOnlyCollection<T>(tempResult);
            context.RecordCopy(input, result);
            return result;
        }
    }
}
