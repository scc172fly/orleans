using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using System;
using System.Collections.Generic;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="SortedSet{T}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class SortedSetCodec<T> : GeneralizedReferenceTypeSurrogateCodec<SortedSet<T>, SortedSetSurrogate<T>>
    {
        /// <summary>
        /// 初始化 <see cref="SortedSetCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
        public SortedSetCodec(IValueSerializer<SortedSetSurrogate<T>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc />
        public override SortedSet<T> ConvertFromSurrogate(ref SortedSetSurrogate<T> surrogate) => new(surrogate.Values, surrogate.Comparer);

        /// <inheritdoc />
        public override void ConvertToSurrogate(SortedSet<T> value, ref SortedSetSurrogate<T> surrogate)
        {
            surrogate.Values = new(value);
            surrogate.Comparer = value.Comparer == Comparer<T>.Default ? null : value.Comparer;
        }
    }

    /// <summary>
    /// <see cref="SortedSetCodec{T}"/> 的代理类型。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [GenerateSerializer]
    public struct SortedSetSurrogate<T>
    {
        /// <summary>
        /// 获取或设置值集合。
        /// </summary>
        /// <value>值集合。</value>
        [Id(0)]
        public List<T> Values;

        /// <summary>
        /// 获取或设置比较器。
        /// </summary>
        /// <value>比较器。</value>
        [Id(1)]
        public IComparer<T>? Comparer;
    }

    /// <summary>
    /// <see cref="SortedSet{T}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class SortedSetCopier<T> : IDeepCopier<SortedSet<T>>, IBaseCopier<SortedSet<T>>
    {
        private readonly Type _fieldType = typeof(SortedSet<T>);
        private readonly IDeepCopier<T> _elementCopier;

        /// <summary>
        /// 初始化 <see cref="SortedSetCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="elementCopier">元素复制器。</param>
        public SortedSetCopier(IDeepCopier<T> elementCopier)
        {
            _elementCopier = elementCopier;
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public SortedSet<T>? DeepCopy(SortedSet<T>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<SortedSet<T>>(input, out var result))
            {
                return result!;
            }

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
            {
                return context.DeepCopy(input);
            }

            result = new SortedSet<T>(input.Comparer);
            context.RecordCopy(input, result);
            foreach (var element in input)
            {
                result.Add(_elementCopier.DeepCopy(element, context)!);
            }

            return result;
        }

        /// <inheritdoc />
        public void DeepCopy(SortedSet<T> input, SortedSet<T> output, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(input);
            ArgumentNullExceptionPolyfill.ThrowIfNull(output);
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);
            foreach (var element in input)
            {
                output.Add(_elementCopier.DeepCopy(element, context)!);
            }
        }
    }
}
