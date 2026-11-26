using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="ConcurrentQueue{T}"/>的序列器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class ConcurrentQueueCodec<T> : GeneralizedReferenceTypeSurrogateCodec<ConcurrentQueue<T>, ConcurrentQueueSurrogate<T>>
    {
        /// <summary>
        /// 初始化 <see cref="ConcurrentQueueCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代孕序列器</param>
        public ConcurrentQueueCodec(IValueSerializer<ConcurrentQueueSurrogate<T>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override ConcurrentQueue<T> ConvertFromSurrogate(ref ConcurrentQueueSurrogate<T> surrogate) => new(surrogate.Values);

        /// <inheritdoc/>
        public override void ConvertToSurrogate(ConcurrentQueue<T> value, ref ConcurrentQueueSurrogate<T> surrogate) => surrogate.Values = new(value);
    }

    /// <summary>
    /// <see cref="ConcurrentQueueCodec{T}"/>所使用的代金矿类型。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [GenerateSerializer]
    public struct ConcurrentQueueSurrogate<T>
    {
        /// <summary>
        /// 获取或设置值。
        /// </summary>
        /// <value>价值。</value>
        [Id(0)]
        public Queue<T> Values;
    }

    /// <summary>
    /// <see cref="ConcurrentQueue{T}"/>的复印机。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [RegisterCopier]
    public sealed class ConcurrentQueueCopier<T> : IDeepCopier<ConcurrentQueue<T>>, IBaseCopier<ConcurrentQueue<T>>
    {
        private readonly Type _fieldType = typeof(ConcurrentQueue<T>);
        private readonly IDeepCopier<T> _copier;

        /// <summary>
        /// 初始化 <see cref="ConcurrentQueueCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="valueCopier">价值复印机。</param>
        public ConcurrentQueueCopier(IDeepCopier<T> valueCopier)
        {
            _copier = valueCopier;
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public ConcurrentQueue<T>? DeepCopy(ConcurrentQueue<T>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<ConcurrentQueue<T>>(input, out var result))
            {
                return result!;
            }

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
            {
                return context.DeepCopy(input);
            }

            result = new ConcurrentQueue<T>();
            context.RecordCopy(input, result);
            foreach (var item in input)
            {
                result.Enqueue(_copier.DeepCopy(item, context)!);
            }

            return result;
        }

        /// <inheritdoc/>
        public void DeepCopy(ConcurrentQueue<T> input, ConcurrentQueue<T> output, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(input);
            ArgumentNullExceptionPolyfill.ThrowIfNull(output);
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            foreach (var item in input)
            {
                output.Enqueue(_copier.DeepCopy(item, context)!);
            }
        }
    }
}
