using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="object"/> 的序列化器。
    /// </summary>
    [RegisterSerializer]
    public sealed class ObjectCodec : IFieldCodec<object>
    {
        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        object IFieldCodec<object>.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field)!;

        /// <summary>
        /// 读取一个值。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        /// <returns>值。</returns>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public static object? ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.IsReference)
            {
                return ReferenceCodec.ReadReference(ref reader, field.FieldType ?? typeof(object));
            }

            if (field.FieldType is null || field.FieldType == typeof(object))
                return ReadObject(ref reader, field);

            var specificSerializer = reader.Session.CodecProvider.GetCodec(field.FieldType);
            return specificSerializer.ReadValue(ref reader, field);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object ReadObject<TInput>(ref Reader<TInput> reader, Field field)
        {
            field.EnsureWireType(WireType.LengthPrefixed);

            var length = reader.ReadVarUInt32();
            if (length != 0) throw new UnexpectedLengthPrefixValueException(nameof(Object), 0, length);

            var result = new object();
            ReferenceCodec.RecordObject(reader.Session, result);
            return result;
        }

        /// <inheritdoc/>
        void IFieldCodec<object>.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] object value) => WriteField(ref writer, fieldIdDelta, expectedType, value);

        /// <summary>
        /// 写入一个字段。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="fieldIdDelta">字段标识符增量。</param>
        /// <param name="expectedType">预期类型。</param>
        /// <param name="value">值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] object? value) where TBufferWriter : IBufferWriter<byte>
        {
            if (value is null)
            {
                ReferenceCodec.WriteNullReference(ref writer, fieldIdDelta);
                return;
            }

            var specificSerializer = writer.Session.CodecProvider.GetCodec(value.GetType());
            specificSerializer.WriteField(ref writer, fieldIdDelta, expectedType, value);
        }

        /// <summary>
        /// 写入一个字段。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="fieldIdDelta">字段标识符增量。</param>
        /// <param name="value">值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, object? value) where TBufferWriter : IBufferWriter<byte>
            => WriteField(ref writer, fieldIdDelta, null!, value);

        void IFieldCodec.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] object? value)
        {
            // 只有无类型写入器才需要支持实际类型为 object 的值。
            if (value is null || value.GetType() == typeof(object))
            {
                if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value!))
                {
                    return;
                }

                writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(object), WireType.LengthPrefixed);
                writer.WriteVarUInt7(0U);
                return;
            }

            var specificSerializer = writer.Session.CodecProvider.GetCodec(value.GetType());
            specificSerializer.WriteField(ref writer, fieldIdDelta, expectedType, value);
        }
    }

    /// <summary>
    /// <see cref="object"/> 的复制器。
    /// </summary>
    [RegisterCopier]
    public sealed class ObjectCopier : IDeepCopier<object>
    {
        /// <summary>
        /// 创建给定输入的深拷贝。
        /// </summary>
        /// <param name="input">输入。</param>
        /// <param name="context">上下文。</param>
        /// <returns><paramref name="input" /> 的副本。</returns>
        [return: NotNullIfNotNull(nameof(input))]
        public static object? DeepCopy(object? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);
            if (input is null) return input;

            return context.TryGetCopy<object>(input, out var result) ? result!
                : input.GetType() == typeof(object) ? input : context.DeepCopy(input);
        }

        object? IDeepCopier<object>.DeepCopy(object? input, CopyContext context)
            => DeepCopy(input, context);

        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context)
            => DeepCopy(input, context);
    }
}
