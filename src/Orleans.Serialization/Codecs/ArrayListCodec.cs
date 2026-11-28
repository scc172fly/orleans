using System;
using System.Collections;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="ArrayList"/> 的序列化器。
    /// </summary>
    [RegisterSerializer]
    public sealed class ArrayListCodec : GeneralizedReferenceTypeSurrogateCodec<ArrayList, ArrayListSurrogate>
    {
        /// <summary>
        /// 初始化 <see cref="ArrayListCodec"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
        public ArrayListCodec(IValueSerializer<ArrayListSurrogate> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override ArrayList ConvertFromSurrogate(ref ArrayListSurrogate surrogate)
            => surrogate.Values is { } values ? new(values) : [];

        /// <inheritdoc/>
        public override void ConvertToSurrogate(ArrayList value, ref ArrayListSurrogate surrogate) => surrogate.Values = value.ToArray();
    }

    /// <summary>
    /// <see cref="ArrayListCodec"/> 所使用的代理类型。
    /// </summary>
    [GenerateSerializer]
    public struct ArrayListSurrogate
    {
        /// <summary>
        /// 获取或设置值集合。
        /// </summary>
        /// <value>值集合。</value>
        [Id(0)]
        public object?[]? Values;
    }

    /// <summary>
    /// <see cref="ArrayList"/> 的复制器。
    /// </summary>
    [RegisterCopier]
    public sealed class ArrayListCopier : IDeepCopier<ArrayList>, IBaseCopier<ArrayList>
    {
        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public ArrayList? DeepCopy(ArrayList? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<ArrayList>(input, out var result))
            {
                return result!;
            }

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() != typeof(ArrayList))
            {
                return context.DeepCopy(input);
            }

            result = new ArrayList(input.Count);
            context.RecordCopy(input, result);
            foreach (var item in input)
            {
                result.Add(ObjectCopier.DeepCopy(item, context));
            }

            return result;
        }

        /// <inheritdoc/>
        public void DeepCopy(ArrayList input, ArrayList output, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(input);
            ArgumentNullExceptionPolyfill.ThrowIfNull(output);
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            foreach (var item in input)
            {
                output.Add(ObjectCopier.DeepCopy(item, context));
            }
        }
    }
}
