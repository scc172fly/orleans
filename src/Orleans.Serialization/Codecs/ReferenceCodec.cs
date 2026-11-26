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
    /// 阅读和编写对象参考资料的功能。
    /// </summary>
    public static class ReferenceCodec
    {
        /// <summary>
        /// 表示正在序列化或去序化的字段是一种数值类型。
        /// </summary>
        /// <param name="session">序列器会话。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MarkValueField(SerializerSession session)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            session.ReferencedObjects.MarkValueField();
        }

        /// <summary>
        /// 如果 <paramref name="value"/> 已经写入, 请写入对象引用 。
        /// 这种超载只适用于固定编码,因为预期的类型是静态的。
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
        /// 如果 <paramref name="value"/> 已经写成,并且通过 <see cref="RecordObject(SerializerSession, object)"/>跟踪了该目标的引用,则写入对象参考。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲写作类型。</typeparam>
        /// <param name="writer">作家。</param>
        /// <param name="fieldId">字段标识符。</param>
        /// <param name="expectedType">预想的类型。</param>
        /// <param name="value">数值。</param>
        /// <returns><see langword="true" /> 如果书面提及,则以其他方式 <see langword="false" /> 。</returns>
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
        /// 写入对象引用 <paramref name="value"/> 已经撰写并跟踪了 <see cref="RecordObject(SerializerSession, object)"/>.        /// 
        /// </summary>
        /// <remarks>此超载允许指定一个用于执行 <see cref="IDerivedTypeCodec"/>的编码器的固定参考类型。</remarks>
        /// <typeparam name="TBufferWriter">缓冲写作类型。</typeparam>
        /// <param name="writer">作家。</param>
        /// <param name="fieldId">字段标识符。</param>
        /// <param name="expectedType">预想的类型。</param>
        /// <param name="actualType">实际类型。</param>
        /// <param name="value">数值。</param>
        /// <returns><see langword="true" /> 如果书面提及,则以其他方式 <see langword="false" /> 。</returns>
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
        /// 写入无效引用。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲写作类型。</typeparam>
        /// <param name="writer">作家。</param>
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
        /// 读取引用值。
        /// </summary>
        /// <typeparam name="T">被引用对象的类型。</typeparam>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <param name="field">字段。</param>
        /// <returns>引用值 。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: MaybeNull]
        public static T ReadReference<T, TInput>(ref Reader<TInput> reader, Field field) => (T)ReadReference(ref reader, field.FieldType ?? typeof(T))!;

        /// <summary>
        /// 阅读参考文献。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <param name="fieldType">字段类型。</param>
        /// <returns>引用值 。</returns>
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
            // 获取状态来自阅读器和会话 。
            var session = reader.Session;
            var originalPosition = reader.Position;
            var referencedObjects = session.ReferencedObjects;
            var originalCurrentReferenceId = referencedObjects.CurrentReferenceId;
            var originalReferenceToObjectCount = referencedObjects.ReferenceToObjectCount;

            // 使对象降序, 替换会话中的标记 。
            try
            {
                // 在标记指定的位置创建阅读器。
                reader.ForkFrom(marker.Position, out var referencedReader);

                // 确定字段的正确类型。
                fieldType = marker.Field.FieldType ?? fieldType;

                // 买一个序列器来做那种类型的
                var specificSerializer = session.CodecProvider.GetCodec(fieldType!);

                // 重置会话的参考代号, 使已消空的天体覆盖占位符标记。
                referencedObjects.CurrentReferenceId = reference - 1;
                referencedObjects.ReferenceToObjectCount = referencedObjects.GetReferenceIndex(marker);
                return specificSerializer.ReadValue(ref referencedReader, marker.Field);
            }
            finally
            {
                // 将引用代号还原。
                referencedObjects.CurrentReferenceId = originalCurrentReferenceId;
                referencedObjects.ReferenceToObjectCount = originalReferenceToObjectCount;
                reader.ResumeFrom(originalPosition);
            }
        }

        /// <summary>
        /// 记录一项物体是读还是写。
        /// </summary>
        /// <param name="session">本届会议。</param>
        /// <param name="value">数值。</param>
        public static void RecordObject(SerializerSession session, object value)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            session.ReferencedObjects.RecordReferenceField(value);
        }

        /// <summary>
        /// 记录一项物体是读还是写。
        /// </summary>
        /// <param name="session">本届会议。</param>
        /// <param name="value">数值。</param>
        /// <param name="referenceId">参考标识符。</param>
        public static void RecordObject(SerializerSession session, object? value, uint referenceId)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            session.ReferencedObjects.RecordReferenceField(value, referenceId);
        }

        /// <summary>
        /// 记录并返回不能立即解除天体飞行的占位符参考代号。
        /// </summary>
        /// <param name="session">本届会议。</param>
        /// <returns>占位符参考代号。</returns>
        public static uint CreateRecordPlaceholder(SerializerSession session)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            return session.ReferencedObjects.CreateRecordPlaceholder();
        }
    }
}
