using Orleans.Serialization.Buffers;
using Orleans.Serialization.Session;
using Orleans.Serialization.WireProtocol;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// 提供读取和写入对象引用的功能。
    /// </summary>
    public static class ReferenceCodec
    {
        /// <summary>
        /// 指示正在序列化或反序列化的字段为值类型。
        /// </summary>
        /// <param name="session">序列化器会话。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MarkValueField(SerializerSession session)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            session.ReferencedObjects.MarkValueField();
        }

        /// <summary>
        /// 如果 <paramref name="value"/> 已经写入，则写入对象引用。
        /// 此重载仅适用于静态编解码器，其预期类型在编译时已知。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryWriteReferenceFieldExpected<TBufferWriter>(
            ref Writer<TBufferWriter> writer,
            uint fieldId,
            [NotNullWhen(false)] object? value) where TBufferWriter : IBufferWriter<byte>
        {
            if (!writer.Session.ReferencedObjects.GetOrAddReference(value, out var reference))
            {
                return false;
            }

            writer.WriteFieldHeaderExpected(fieldId, WireType.Reference);
            writer.WriteVarUInt32(reference);
            return true;
        }

        /// <summary>
        /// 如果 <paramref name="value"/> 已写入且已通过 <see cref="RecordObject(SerializerSession, object)"/> 跟踪，则写入对象引用。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="fieldId">字段标识符。</param>
        /// <param name="expectedType">预期类型。</param>
        /// <param name="value">值。</param>
        /// <returns><see langword="true" /> 表示已写入引用，否则为 <see langword="false" />。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteReferenceField<TBufferWriter>(
            ref Writer<TBufferWriter> writer,
            uint fieldId,
            Type? expectedType,
            [NotNullWhen(false)] object? value) where TBufferWriter : IBufferWriter<byte>
        {
            if (!writer.Session.ReferencedObjects.GetOrAddReference(value, out var reference))
            {
                return false;
            }

            writer.WriteFieldHeader(fieldId, expectedType, value?.GetType(), WireType.Reference);
            writer.WriteVarUInt32(reference);
            return true;
        }

        /// <summary>
        /// 如果 <paramref name="value"/> 已写入且已通过 <see cref="RecordObject(SerializerSession, object)"/> 跟踪，则写入对象引用。
        /// </summary>
        /// <remarks>此重载允许为实现 <see cref="IDerivedTypeCodec"/> 的编解码器指定固定的引用类型。</remarks>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="fieldId">字段标识符。</param>
        /// <param name="expectedType">预期类型。</param>
        /// <param name="actualType">实际类型。</param>
        /// <param name="value">值。</param>
        /// <returns><see langword="true" /> 表示已写入引用，否则为 <see langword="false" />。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteReferenceField<TBufferWriter>(
            ref Writer<TBufferWriter> writer,
            uint fieldId,
            Type? expectedType,
            Type actualType,
            [NotNullWhen(false)] object? value) where TBufferWriter : IBufferWriter<byte>
        {
            if (!writer.Session.ReferencedObjects.GetOrAddReference(value, out var reference))
            {
                return false;
            }

            writer.WriteFieldHeader(fieldId, expectedType, value is null ? null : actualType, WireType.Reference);
            writer.WriteVarUInt32(reference);
            return true;
        }

        /// <summary>
        /// 写入 null 引用。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="fieldId">字段标识符。</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void WriteNullReference<TBufferWriter>(
            ref Writer<TBufferWriter> writer,
            uint fieldId) where TBufferWriter : IBufferWriter<byte>
        {
            writer.Session.ReferencedObjects.MarkValueField();
            writer.WriteFieldHeaderExpected(fieldId, WireType.Reference);
            writer.WriteByte(1); // writer.WriteVarUInt32(0U);
        }

        /// <summary>
        /// 读取被引用的值。
        /// </summary>
        /// <typeparam name="T">被引用对象的类型。</typeparam>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        /// <returns>被引用的值。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: MaybeNull]
        public static T ReadReference<T, TInput>(ref Reader<TInput> reader, Field field) => (T)ReadReference(ref reader, field.FieldType ?? typeof(T))!;

        /// <summary>
        /// 读取引用。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="fieldType">字段类型。</param>
        /// <returns>被引用的值。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static object? ReadReference<TInput>(ref Reader<TInput> reader, Type? fieldType)
        {
            MarkValueField(reader.Session);
            var reference = reader.ReadVarUInt32();
            if (reference == 0)
                return null;

            return ReadReference(ref reader, fieldType, reference);
        }

        private static object? ReadReference<TInput>(ref Reader<TInput> reader, Type? fieldType, uint reference)
        {
            var value = reader.Session.ReferencedObjects.TryGetReferencedObject(reference);
            if (value is null) throw new ReferenceNotFoundException(fieldType, reference);

            return value switch
            {
                UnknownFieldMarker marker => DeserializeFromMarker(ref reader, fieldType, marker, reference),
                _ => value,
            };
        }

        private static object? DeserializeFromMarker<TInput>(
            ref Reader<TInput> reader,
            Type? fieldType,
            UnknownFieldMarker marker,
            uint reference)
        {
            // 捕获读取器及会话的状态。
            var session = reader.Session;
            var originalPosition = reader.Position;
            var referencedObjects = session.ReferencedObjects;
            var originalCurrentReferenceId = referencedObjects.CurrentReferenceId;
            var originalReferenceToObjectCount = referencedObjects.ReferenceToObjectCount;

            // 反序列化对象，替换会话中的标记。
            try
            {
                // 在标记指定的位置创建读取器。
                reader.ForkFrom(marker.Position, out var referencedReader);

                // 确定字段的正确类型。
                fieldType = marker.Field.FieldType ?? fieldType;

                // 获取该类型的序列化器。
                var specificSerializer = session.CodecProvider.GetCodec(fieldType!);

                // 重置会话的引用 ID，使反序列化后的对象覆盖占位标记。
                referencedObjects.CurrentReferenceId = reference - 1;
                referencedObjects.ReferenceToObjectCount = referencedObjects.GetReferenceIndex(marker);
                return specificSerializer.ReadValue(ref referencedReader, marker.Field);
            }
            finally
            {
                // 恢复引用 ID。
                referencedObjects.CurrentReferenceId = originalCurrentReferenceId;
                referencedObjects.ReferenceToObjectCount = originalReferenceToObjectCount;
                reader.ResumeFrom(originalPosition);
            }
        }

        /// <summary>
        /// 记录对象已被读取或写入。
        /// </summary>
        /// <param name="session">会话。</param>
        /// <param name="value">值。</param>
        public static void RecordObject(SerializerSession session, object value)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            session.ReferencedObjects.RecordReferenceField(value);
        }

        /// <summary>
        /// 记录对象已被读取或写入。
        /// </summary>
        /// <param name="session">会话。</param>
        /// <param name="value">值。</param>
        /// <param name="referenceId">引用标识符。</param>
        public static void RecordObject(SerializerSession session, object? value, uint referenceId)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            session.ReferencedObjects.RecordReferenceField(value, referenceId);
        }

        /// <summary>
        /// 为无法立即反序列化的对象记录并返回占位引用 ID。
        /// </summary>
        /// <param name="session">会话。</param>
        /// <returns>占位引用 ID。</returns>
        public static uint CreateRecordPlaceholder(SerializerSession session)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            return session.ReferencedObjects.CreateRecordPlaceholder();
        }
    }
}
