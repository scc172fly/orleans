using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Orleans.Serialization.WireProtocol
{
    /// <summary>
    /// 代表外地信头。
    /// </summary>
    public struct Field
    {
        /// <summary>
        /// 标签字节
        /// </summary>
        public Tag Tag;

        /// <summary>
        /// 原始字段识别符 三角形 。
        /// </summary>
        public uint FieldIdDeltaRaw;

        /// <summary>
        /// 原始田地类型。
        /// </summary>
        public Type? FieldTypeRaw;

        /// <summary>
        /// 初始化 <see cref="Field"/> 结构的新实例。
        /// </summary>
        /// <param name="tag">标签。</param>
        public Field(Tag tag)
        {
            Tag = tag;
            FieldIdDeltaRaw = tag.FieldIdDelta;
            FieldTypeRaw = null;
        }

        /// <summary>
        /// 初始化 <see cref="Field"/> 结构的新实例。
        /// </summary>
        /// <param name="tag">标签。</param>
        /// <param name="extendedFieldIdDelta">扩展的字段识别符号三角形。</param>
        /// <param name="type">类型。</param>
        public Field(Tag tag, uint extendedFieldIdDelta, Type type)
        {
            Tag = tag;
            FieldIdDeltaRaw = extendedFieldIdDelta;
            FieldTypeRaw = type;
        }

        /// <summary>
        /// 获取或设置字段识别符三角形。
        /// </summary>
        /// <value>字段识别符号三角形。</value>
        public uint FieldIdDelta
        {
            // 如果嵌入字段 id 三角洲有效, 请返回它, 否则则返回扩展字段 id 三角洲 。
            // 如果扩展字段有扩展线型, 则扩展域代号可能无效 。
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
                // 如果字段 id 三角洲可以融入标记, 嵌入标签中, 否则将嵌入的字段 id 三角洲无效, 并设置整个字段 id 三角洲 。
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
        /// 获取或设置字段的类型。
        /// </summary>
        /// <value>字段的类型。</value>
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
        /// 获得一个值,以显示此实例是否有字段标识符。
        /// </summary>
        /// <value><see langword="true" /> ,如果此实例中有一个字段标识符;否则, <see langword="false" />。</value>
        public readonly bool HasFieldId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !Tag.HasExtendedWireType;
        }

        /// <summary>
        /// 获得一个值,以显示此实例是否有扩展字段标识符。
        /// </summary>
        /// <value><see langword="true" /> 如果此实例有扩展字段标识符;否则, <see langword="false" />。</value>
        public readonly bool HasExtendedFieldId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Tag.HasExtendedFieldId;
        }

        /// <summary>
        /// 接线或设置电线型号
        /// </summary>
        /// <value>电线型号。</value>
        public WireType WireType
        {
            readonly get => Tag.WireType;
            set => Tag.WireType = value;
        }

        /// <summary>
        /// 设置或设置模式类型。
        /// </summary>
        /// <value>型号。</value>
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
        /// 获取或设置扩展的线型。
        /// </summary>
        /// <value>长线型</value>
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
        /// 获得一个值,以显示此实例是否具有有效的 schema 类型。
        /// </summary>
        /// <value><see langword="true" /> 如果这一案例有有效的计划,否则 <see langword="false" />.</value>
        public readonly bool IsSchemaTypeValid => Tag.IsSchemaTypeValid;

        /// <summary>
        /// 获得一个值, 显示此实例是否有扩展的 schema 类型 。
        /// </summary>
        /// <value><see langword="true" /> ,如果这种情况具有扩展的方形类型;否则, <see langword="false" />。</value>
        public readonly bool HasExtendedSchemaType => Tag.IsSchemaTypeValid && Tag.SchemaType != SchemaType.Expected;

        /// <summary>
        /// 获得一个值, 显示此实例是否代表标签限制结构中基字段的结束。
        /// </summary>
        /// <value><see langword="true" /> 如果此实例代表了标签上限定结构中基字段的结束;否则, <see langword="false" />。</value>
        public readonly bool IsEndBaseFields => Tag.IsEndBaseFields;

        /// <summary>
        /// 获得一个值, 显示此实例是否代表标签限制结构的结束 。
        /// </summary>
        /// <value><see langword="true" /> 如果此实例代表了标签限定结构的结束; 否则, <see langword="false" />。</value>
        public readonly bool IsEndObject => Tag.IsEndObject;

        /// <summary>
        /// 获得一个值, 显示此实例是代表标签限制结构的结尾还是标记限制结构中基字段的终点。
        /// </summary>
        /// <value><see langword="true" /> 如果此实例代表标签上的限制结构的结束或标记上的限制结构中基字段的结束; 否则, <see langword="false" />。</value>
        public readonly bool IsEndBaseOrEndObject
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Tag.HasExtendedWireType/* && Tag.ExtendedWireType <= ExtendedWireType.EndBaseFields*/;
        }

        /// <summary>
        /// 获得一个值,以显示此实例是否有 <see cref="WireType.Reference"/>的线型。
        /// </summary>
        public readonly bool IsReference => Tag.WireType == WireType.Reference;

        /// <summary>
        /// 确保电线型号为 <see cref="WireType.TagDelimited"/>。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureWireTypeTagDelimited()
        {
            if (Tag.WireType != WireType.TagDelimited)
                UnsupportedWireType();
        }

        private void UnsupportedWireType() => throw new UnsupportedWireTypeException($"A WireType value of {nameof(WireType.TagDelimited)} is expected by this codec. {this}");

        /// <summary>
        /// 确保支持电线型号。
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
