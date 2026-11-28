using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Orleans.Serialization.WireProtocol
{
    /// <summary>
    /// 表示字段头。
    /// </summary>
    public struct Field
    {
        /// <summary>
        /// 标记字节。
        /// </summary>
        public Tag Tag;

        /// <summary>
        /// 原始字段 ID 差值。
        /// </summary>
        public uint FieldIdDeltaRaw;

        /// <summary>
        /// 原始字段类型。
        /// </summary>
        public Type? FieldTypeRaw;

        /// <summary>
        /// 初始化 <see cref="Field"/> 结构的新实例。
        /// </summary>
        /// <param name="tag">标记。</param>
        public Field(Tag tag)
        {
            Tag = tag;
            FieldIdDeltaRaw = tag.FieldIdDelta;
            FieldTypeRaw = null;
        }

        /// <summary>
        /// 初始化 <see cref="Field"/> 结构的新实例。
        /// </summary>
        /// <param name="tag">标记。</param>
        /// <param name="extendedFieldIdDelta">扩展字段 ID 差值。</param>
        /// <param name="type">类型。</param>
        public Field(Tag tag, uint extendedFieldIdDelta, Type type)
        {
            Tag = tag;
            FieldIdDeltaRaw = extendedFieldIdDelta;
            FieldTypeRaw = type;
        }

        /// <summary>
        /// 获取或设置字段 ID 差值。
        /// </summary>
        /// <value>字段 ID 差值。</value>
        public uint FieldIdDelta
        {
            // 如果内嵌字段 ID 差值有效，则返回该值；否则返回扩展字段 ID 差值。
            // 如果此字段的传输格式类型为 Extended，则扩展字段 ID 可能无效。
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get
            {
#if DEBUG
                if (!HasFieldId) throw new FieldIdNotPresentException();
#endif
                return FieldIdDeltaRaw;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                // 如果字段 ID 差值能容纳在标记中，则将它嵌入标记；否则将内嵌字段 ID 差值标记为无效，并设置完整字段 ID 差值。
                if (value <= Tag.MaxEmbeddedFieldIdDelta)
                {
                    Tag.FieldIdDelta = value;
                }
                else
                {
                    Tag.SetFieldIdInvalid();
                }
                FieldIdDeltaRaw = value;
            }
        }

        /// <summary>
        /// 获取或设置字段类型。
        /// </summary>
        /// <value>字段类型。</value>
        public Type? FieldType
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get
            {
#if DEBUG
                if (!IsSchemaTypeValid)
                {
                    throw new FieldTypeInvalidException();
                }
#endif
                return FieldTypeRaw;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
#if DEBUG
                if (!IsSchemaTypeValid)
                {
                    throw new FieldTypeInvalidException();
                }
#endif
                FieldTypeRaw = value;
            }
        }

        /// <summary>
        /// 获取一个值，指示此实例是否具有字段标识符。
        /// </summary>
        /// <value><see langword="true" />，如果此实例具有字段标识符；否则为 <see langword="false" />。</value>
        public readonly bool HasFieldId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !Tag.HasExtendedWireType;
        }

        /// <summary>
        /// 获取一个值，指示此实例是否具有扩展字段标识符。
        /// </summary>
        /// <value><see langword="true" />，如果此实例具有扩展字段标识符；否则为 <see langword="false" />。</value>
        public readonly bool HasExtendedFieldId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Tag.HasExtendedFieldId;
        }

        /// <summary>
        /// 获取或设置传输格式类型。
        /// </summary>
        /// <value>传输格式类型。</value>
        public WireType WireType
        {
            readonly get => Tag.WireType;
            set => Tag.WireType = value;
        }

        /// <summary>
        /// 获取或设置架构类型。
        /// </summary>
        /// <value>架构类型。</value>
        public SchemaType SchemaType
        {
            readonly get
            {
#if DEBUG
                if (!IsSchemaTypeValid)
                {
                    throw new SchemaTypeInvalidException();
                }
#endif

                return Tag.SchemaType;
            }

            set => Tag.SchemaType = value;
        }

        /// <summary>
        /// 获取或设置扩展传输格式类型。
        /// </summary>
        /// <value>扩展传输格式类型。</value>
        public ExtendedWireType ExtendedWireType
        {
            readonly get
            {
#if DEBUG
                if (WireType != WireType.Extended)
                {
                    throw new ExtendedWireTypeInvalidException();
                }
#endif
                return Tag.ExtendedWireType;
            }
            set => Tag.ExtendedWireType = value;
        }

        /// <summary>
        /// 获取一个值，指示此实例的架构类型是否有效。
        /// </summary>
        /// <value><see langword="true" />，如果此实例的架构有效；否则为 <see langword="false" />。</value>
        public readonly bool IsSchemaTypeValid => Tag.IsSchemaTypeValid;

        /// <summary>
        /// 获取一个值，指示此实例是否具有扩展架构类型。
        /// </summary>
        /// <value><see langword="true" />，如果此实例具有扩展架构类型；否则为 <see langword="false" />。</value>
        public readonly bool HasExtendedSchemaType => Tag.IsSchemaTypeValid && Tag.SchemaType != SchemaType.Expected;

        /// <summary>
        /// 获取一个值，指示此实例是否表示标记分隔结构中基类型字段的结束位置。
        /// </summary>
        /// <value><see langword="true" />，如果此实例表示标记分隔结构中基类型字段的结束位置；否则为 <see langword="false" />。</value>
        public readonly bool IsEndBaseFields => Tag.IsEndBaseFields;

        /// <summary>
        /// 获取一个值，指示此实例是否表示标记分隔结构的结束位置。
        /// </summary>
        /// <value><see langword="true" />，如果此实例表示标记分隔结构的结束位置；否则为 <see langword="false" />。</value>
        public readonly bool IsEndObject => Tag.IsEndObject;

        /// <summary>
        /// 获取一个值，指示此实例是否表示标记分隔结构的结束位置或该结构中基类型字段的结束位置。
        /// </summary>
        /// <value><see langword="true" />，如果此实例表示标记分隔结构的结束位置或其中基类型字段的结束位置；否则为 <see langword="false" />。</value>
        public readonly bool IsEndBaseOrEndObject
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Tag.HasExtendedWireType/* && Tag.ExtendedWireType <= ExtendedWireType.EndBaseFields*/;
        }

        /// <summary>
        /// 获取一个值，指示此实例的传输格式类型是否为 <see cref="WireType.Reference"/>。
        /// </summary>
        public readonly bool IsReference => Tag.WireType == WireType.Reference;

        /// <summary>
        /// 确保传输格式类型为 <see cref="WireType.TagDelimited"/>。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureWireTypeTagDelimited()
        {
            if (Tag.WireType != WireType.TagDelimited)
                UnsupportedWireType();
        }

        private void UnsupportedWireType() => throw new UnsupportedWireTypeException($"A WireType value of {nameof(WireType.TagDelimited)} is expected by this codec. {this}");

        /// <summary>
        /// 确保支持该传输格式类型。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureWireType(WireType expectedType)
        {
            if (Tag.WireType != expectedType)
                UnsupportedWireType(expectedType);
        }

        private void UnsupportedWireType(WireType expectedType) => throw new UnsupportedWireTypeException($"A WireType value of {expectedType} is expected by this codec. {this}");

        /// <inheritdoc/>
        public override string ToString()
        {
#if NET6_0_OR_GREATER
            var builder = new DefaultInterpolatedStringHandler(0, 0);
            builder.AppendLiteral("[");
            builder.AppendFormatted(WireType);

            if (HasFieldId)
            {
                builder.AppendLiteral(", IdDelta:");
                builder.AppendFormatted(FieldIdDelta);
            }

            if (IsSchemaTypeValid)
            {
                builder.AppendLiteral(", SchemaType:");
                builder.AppendFormatted(SchemaType);
            }

            if (HasExtendedSchemaType)
            {
                builder.AppendLiteral(", RuntimeType:");
                builder.AppendFormatted(FieldType);
            }

            if (Tag.HasExtendedWireType)
            {
                builder.AppendLiteral(": ");
                builder.AppendFormatted(ExtendedWireType);
            }

            builder.AppendLiteral("]");
            return builder.ToStringAndClear();
#else
            var builder = new StringBuilder();
            builder.Append('[');
            builder.Append(WireType);

            if (HasFieldId)
            {
                builder.Append(", IdDelta:");
                builder.Append(FieldIdDelta);
            }

            if (IsSchemaTypeValid)
            {
                builder.Append(", SchemaType:");
                builder.Append(SchemaType);
            }

            if (HasExtendedSchemaType)
            {
                builder.Append(", RuntimeType:");
                builder.Append(FieldType);
            }

            if (WireType == WireType.Extended)
            {
                builder.Append(": ");
                builder.Append(ExtendedWireType);
            }

            builder.Append(']');
            return builder.ToString();
#endif
        }
    }
}
