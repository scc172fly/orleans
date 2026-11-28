using System;
using System.Runtime.CompilerServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.TypeSystem;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Buffers
{
    public ref partial struct Writer<TBufferWriter>
    {
        /// <summary>
        /// 写入字段头。
        /// </summary>
        /// <param name="fieldId">字段标识符。</param>
        /// <param name="expectedType">预期类型。</param>
        /// <param name="actualType">实际类型。</param>
        /// <param name="wireType">传输格式类型。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFieldHeader(uint fieldId, Type? expectedType, Type? actualType, WireType wireType)
        {
            var embeddedFieldId = fieldId > Tag.MaxEmbeddedFieldIdDelta ? Tag.FieldIdCompleteMask : fieldId;
            var tag = (uint)wireType | embeddedFieldId;

            if (actualType is null || actualType == expectedType)
            {
                WriteByte((byte)tag); // SchemaType.Expected=0
                if (fieldId > Tag.MaxEmbeddedFieldIdDelta)
                {
                    WriteVarUInt32(fieldId);
                }
                return;
            }

            uint typeOrReferenceId;
            if (Session.WellKnownTypes.TryGetWellKnownTypeId(actualType, out var typeId))
            {
                typeOrReferenceId = typeId;
                tag |= (byte)SchemaType.WellKnown;
            }
            else if ((typeOrReferenceId = Session.ReferencedTypes.GetOrAddTypeReference(actualType)) != 0)
            {
                tag |= (byte)SchemaType.Referenced;
            }
            else
            {
                WriteFieldHeaderEncodeType(fieldId, actualType, tag);
                return;
            }

            WriteByte((byte)tag);
            if (fieldId > Tag.MaxEmbeddedFieldIdDelta)
                WriteVarUInt32(fieldId);

            WriteVarUInt32(typeOrReferenceId);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteFieldHeaderEncodeType(uint fieldId, Type actualType, uint tag)
        {
            WriteByte((byte)(tag | (byte)SchemaType.Encoded));
            if (fieldId > Tag.MaxEmbeddedFieldIdDelta)
                WriteVarUInt32(fieldId);

            Session.TypeCodec.WriteEncodedType(ref this, actualType);
        }

        /// <summary>
        /// 写入预期类型的字段头值。
        /// </summary>
        /// <param name="fieldId">字段标识符。</param>
        /// <param name="wireType">字段的传输格式类型。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFieldHeaderExpected(uint fieldId, WireType wireType)
        {
            var embeddedFieldId = fieldId > Tag.MaxEmbeddedFieldIdDelta ? Tag.FieldIdCompleteMask : fieldId;
            WriteByte((byte)((uint)wireType | embeddedFieldId));

            if (fieldId > Tag.MaxEmbeddedFieldIdDelta)
                WriteVarUInt32(fieldId);
        }
    }
}

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// 用于处理传输格式的编解码器。
    /// </summary>
    public static class FieldHeaderCodec
    {
        /// <summary>
        /// 读取字段头。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段头。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadFieldHeader<TInput>(ref this Reader<TInput> reader, scoped ref Field field)
        {
            var tag = (uint)reader.ReadByte();
            field.Tag = new(tag);
            // 如果需要 ID 或架构类型，且它们未编码在标记中，则读取扩展头数据。
            if (tag < (byte)WireType.Extended && (tag & (Tag.FieldIdCompleteMask | Tag.SchemaTypeMask)) >= Tag.FieldIdCompleteMask)
            {
                ReadExtendedFieldHeader(ref reader, ref field);
            }
            else
            {
                field.FieldIdDeltaRaw = tag & Tag.FieldIdMask;
                field.FieldTypeRaw = default;
            }
        }

        /// <summary>
        /// 读取字段头。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <returns>字段头。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Field ReadFieldHeader<TInput>(ref this Reader<TInput> reader)
        {
            Unsafe.SkipInit(out Field field);
            var tag = (uint)reader.ReadByte();
            field.Tag = new(tag);
            // 如果需要 ID 或架构类型，且它们未编码在标记中，则读取扩展头数据。
            if (tag < (byte)WireType.Extended && (tag & (Tag.FieldIdCompleteMask | Tag.SchemaTypeMask)) >= Tag.FieldIdCompleteMask)
            {
                ReadExtendedFieldHeader(ref reader, ref field);
            }
            else
            {
                field.FieldIdDeltaRaw = tag & Tag.FieldIdMask;
                field.FieldTypeRaw = default;
            }

            return field;
        }

        /// <summary>
        /// 读取扩展字段头。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReadExtendedFieldHeader<TInput>(ref this Reader<TInput> reader, scoped ref Field field)
        {
            var fieldId = field.Tag.FieldIdDelta;
            if (fieldId == Tag.FieldIdCompleteMask)
            {
                field.FieldIdDeltaRaw = reader.ReadVarUInt32NoInlining();
            }
            else
            {
                field.FieldIdDeltaRaw = fieldId;
            }

            // 如果架构类型有效，则读取类型。
            var schemaType = field.Tag.SchemaType;
            if (schemaType != SchemaType.Expected)
            {
                field.FieldTypeRaw = reader.ReadType(schemaType);
            }
            else
            {
                field.FieldTypeRaw = default;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Type? ReadType<TInput>(this ref Reader<TInput> reader, SchemaType schemaType)
        {
            switch (schemaType)
            {
                case SchemaType.WellKnown:
                    var typeId = reader.ReadVarUInt32();
                    return reader.Session.WellKnownTypes.GetWellKnownType(typeId);
                case SchemaType.Encoded:
                    var encoded = reader.Session.TypeCodec.TryRead(ref reader);
                    reader.Session.ReferencedTypes.RecordReferencedType(encoded);
                    return encoded;
                case SchemaType.Referenced:
                    var reference = reader.ReadVarUInt32();
                    return reader.Session.ReferencedTypes.GetReferencedType(reference);
                default:
                    return ExceptionHelper.ThrowArgumentOutOfRange<Type>(nameof(SchemaType));
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (Type? type, string typeName) ReadTypeForAnalysis<TInput>(this ref Reader<TInput> reader, SchemaType schemaType)
        {
            switch (schemaType)
            {
                case SchemaType.WellKnown:
                    {
                        var typeId = reader.ReadVarUInt32();
                        var found = reader.Session.WellKnownTypes.TryGetWellKnownType(typeId, out var type);
                        return (type, $"WellKnown {typeId} ({(found ? type is null ? "null" : RuntimeTypeNameFormatter.Format(type) : "unknown")})");
                    }
                case SchemaType.Encoded:
                    {
                        var found = reader.Session.TypeCodec.TryReadForAnalysis(ref reader, out var encoded, out var typeString);
                        reader.Session.ReferencedTypes.RecordReferencedType(encoded);
                        return (encoded, $"Encoded \"{typeString}\" ({(found ? encoded is null ? "null" : RuntimeTypeNameFormatter.Format(encoded) : "not found")})");
                    }
                case SchemaType.Referenced:
                    {
                        var reference = reader.ReadVarUInt32();
                        var found = reader.Session.ReferencedTypes.TryGetReferencedType(reference, out var type);
                        return (type, $"Referenced {reference} ({(found ? type is null ? "null" : RuntimeTypeNameFormatter.Format(type) : "not found")})");
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(schemaType));
            }
        }

        /// <summary>
        /// 读取字段头，用于诊断。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <returns>读取到的值。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (Field Field, string Type) ReadFieldHeaderForAnalysis<TInput>(ref this Reader<TInput> reader)
        {
            Unsafe.SkipInit(out Field field);
            string type = default!;
            var tag = (uint)reader.ReadByte();
            field.Tag = new(tag);
            if (tag < (byte)WireType.Extended && ((tag & Tag.FieldIdCompleteMask) == Tag.FieldIdCompleteMask || (tag & Tag.SchemaTypeMask) != (byte)SchemaType.Expected))
            {
                ReadFieldHeaderForAnalysisSlow(ref reader, ref field, ref type);
            }
            else
            {
                field.FieldIdDeltaRaw = tag & Tag.FieldIdMask;
                field.FieldTypeRaw = default;
                type = "Expected";
            }

            return (field, type);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReadFieldHeaderForAnalysisSlow<TInput>(ref this Reader<TInput> reader, scoped ref Field field, scoped ref string type)
        {
            var fieldId = field.Tag.FieldIdDelta;
            if (fieldId == Tag.FieldIdCompleteMask)
            {
                field.FieldIdDeltaRaw = reader.ReadVarUInt32NoInlining();
            }
            else
            {
                field.FieldIdDeltaRaw = fieldId;
            }

            // 如果架构类型有效，则读取类型。
            var schemaType = field.Tag.SchemaType;
            if (schemaType != SchemaType.Expected)
            {
                (field.FieldTypeRaw, type) = reader.ReadTypeForAnalysis(schemaType);
            }
            else
            {
                field.FieldTypeRaw = default;
                type = "Expected";
            }
        }
    }
}
