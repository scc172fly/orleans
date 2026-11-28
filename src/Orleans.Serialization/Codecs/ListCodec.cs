using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="List{T}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class ListCodec<T> : IFieldCodec<List<T>>, IBaseCodec<List<T>>
    {
        private readonly Type CodecElementType = typeof(T);

        private readonly IFieldCodec<T> _fieldCodec;

        /// <summary>
        /// 初始化 <see cref="ListCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="fieldCodec">字段编解码器。</param>
        public ListCodec(IFieldCodec<T> fieldCodec)
        {
            _fieldCodec = OrleansGeneratedCodeHelper.UnwrapService(this, fieldCodec);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] List<T> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            System.Diagnostics.Debug.Assert(value is not null);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            Serialize(ref writer, value);

            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public List<T> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<List<T>, TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            List<T>? result = null;
            uint fieldId = 0;
            while (true)
            {
                var header = reader.ReadFieldHeader();
                if (header.IsEndBaseOrEndObject)
                {
                    break;
                }

                fieldId += header.FieldIdDelta;
                switch (fieldId)
                {
                    case 0:
                        var length = (int)UInt32Codec.ReadValue(ref reader, header);
                        reader.EnsureAvailable((uint)length);

                        result = new(length);
                        ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
                        break;
                    case 1:
                        if (result is null)
                        {
                            ListCodec<T>.ThrowLengthFieldMissing();
                        }

                        result!.Add(_fieldCodec.ReadValue(ref reader, header)!);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            if (result is null)
            {
                result = new();
                ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
            }

            return result!;
        }

        private static void ThrowLengthFieldMissing() => throw new RequiredFieldMissingException("Serialized array is missing its length field.");

        /// <inheritdoc />
        public void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, List<T> value) where TBufferWriter : IBufferWriter<byte>
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(value);

            if (value.Count > 0)
            {
                UInt32Codec.WriteField(ref writer, 0, (uint)value.Count);
                uint innerFieldIdDelta = 1;
                foreach (var element in value)
                {
                    _fieldCodec.WriteField(ref writer, innerFieldIdDelta, CodecElementType, element);
                    innerFieldIdDelta = 0;
                }
            }
        }

        /// <inheritdoc />
        public void Deserialize<TInput>(ref Reader<TInput> reader, List<T> value)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(value);

            // 如果集合中包含构造函数添加的元素，则将其清空。
            // 如果序列化有效载荷中包含这些元素，则会在后续代码中重新添加它们。
            value.Clear();

            uint fieldId = 0;
            while (true)
            {
                var header = reader.ReadFieldHeader();
                if (header.IsEndBaseOrEndObject)
                {
                    break;
                }

                fieldId += header.FieldIdDelta;
                switch (fieldId)
                {
                    case 0:
                        var length = (int)UInt32Codec.ReadValue(ref reader, header);
                        reader.EnsureAvailable((uint)length);

#if NET6_0_OR_GREATER
                        value.EnsureCapacity(length);
#endif
                        break;
                    case 1:
                        value.Add(_fieldCodec.ReadValue(ref reader, header)!);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// <see cref="List{T}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class ListCopier<T> : IDeepCopier<List<T>>, IBaseCopier<List<T>>
    {
        private readonly IDeepCopier<T> _copier;

        /// <summary>
        /// 初始化 <see cref="ListCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="valueCopier">元素复制器。</param>
        public ListCopier(IDeepCopier<T> valueCopier)
        {
            _copier = OrleansGeneratedCodeHelper.UnwrapService(this, valueCopier);
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public List<T>? DeepCopy(List<T>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<List<T>>(input, out var result))
            {
                return result!;
            }

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() != typeof(List<T>))
            {
                return context.DeepCopy(input);
            }

            result = new List<T>(input.Count);
            context.RecordCopy(input, result);
            foreach (var item in input)
            {
                result.Add(_copier.DeepCopy(item, context)!);
            }

            return result;
        }

        /// <inheritdoc/>
        public void DeepCopy(List<T> input, List<T> output, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(input);
            ArgumentNullExceptionPolyfill.ThrowIfNull(output);
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            output.Clear();

#if NET6_0_OR_GREATER
            output.EnsureCapacity(input.Count);
#endif
            foreach (var item in input)
            {
                output.Add(_copier.DeepCopy(item, context)!);
            }
        }
    }
}
