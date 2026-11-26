using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace Orleans.Serialization
{
    internal static class ExceptionHelper
    {
        public static T ThrowArgumentOutOfRange<T>(string argument) => throw new ArgumentOutOfRangeException(argument);
    }

    /// <summary>
    /// 任何序列器例外的基底例外。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class SerializerException : Exception
    {
        /// <summary>
        /// 初始化 <see cref="SerializerException"/> 类的新实例。
        /// </summary>
        public SerializerException()
        {
        }

        /// <summary>
        /// 初始化 <see cref="SerializerException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的电文。</param>
        public SerializerException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="SerializerException"/> 类的新实例。
        /// </summary>
        /// <param name="message">解释例外原因的错误信息。</param>
        /// <param name="innerException">造成目前例外或无效引用的例外(<see langword="Nothing" /> ),如果未指明内侧例外。</param>
        public SerializerException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="SerializerException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        protected SerializerException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 期望有一个字段识别符, 但该标识符不存在 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class FieldIdNotPresentException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="FieldIdNotPresentException"/> 类的新实例。
        /// </summary>
        public FieldIdNotPresentException() : base("Attempted to access the field id from a tag which cannot have a field id.")
        {
        }

        /// <summary>
        /// 初始化 <see cref="FieldIdNotPresentException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private FieldIdNotPresentException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// schema 类型无效 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class SchemaTypeInvalidException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="SchemaTypeInvalidException"/> 类的新实例。
        /// </summary>
        public SchemaTypeInvalidException() : base("Attempted to access the schema type from a tag which cannot have a schema type.")
        {
        }

        /// <summary>
        /// 初始化 <see cref="SchemaTypeInvalidException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private SchemaTypeInvalidException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 字段类型无效 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class FieldTypeInvalidException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="FieldTypeInvalidException"/> 类的新实例。
        /// </summary>
        public FieldTypeInvalidException() : base("Attempted to access the schema type from a tag which cannot have a schema type.")
        {
        }

        /// <summary>
        /// 初始化 <see cref="FieldTypeInvalidException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private FieldTypeInvalidException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 期望字段类型不存在。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class FieldTypeMissingException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="FieldTypeMissingException"/> 类的新实例。
        /// </summary>
        /// <param name="type">类型。</param>
        public FieldTypeMissingException(Type type) : base($"Attempted to deserialize an instance of abstract type {type}. No concrete type was provided.")
        {
        }

        /// <summary>
        /// 初始化 <see cref="FieldTypeMissingException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private FieldTypeMissingException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 连环贴标签的工会价值确定工会类型或封闭型等级没有申报的情况。
    /// </summary>
    /// <remarks>
    /// 当由一种类型较新版本的、宣布额外案例的数据编写数据时,就会出现这种情况。
    /// </remarks>
    [Serializable]
    [GenerateSerializer]
    public sealed class UnknownUnionCaseException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="UnknownUnionCaseException"/> 类的新实例。
        /// </summary>
        /// <param name="unionType">工会类型或封闭型基底类型正在被解除输电。</param>
        /// <param name="caseId">所读到的案例识别符号 。</param>
        public UnknownUnionCaseException(Type unionType, uint caseId) : base($"Encountered unknown case {caseId} while deserializing a value of type {unionType}.")
        {
        }

        /// <summary>
        /// 初始化 <see cref="UnknownUnionCaseException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private UnknownUnionCaseException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 扩展线型无效 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class ExtendedWireTypeInvalidException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="ExtendedWireTypeInvalidException"/> 类的新实例。
        /// </summary>
        public ExtendedWireTypeInvalidException() : base(
            "Attempted to access the extended wire type from a tag which does not have an extended wire type.")
        {
        }

        /// <summary>
        /// 初始化 <see cref="ExtendedWireTypeInvalidException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private ExtendedWireTypeInvalidException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 线型没有支持 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class UnsupportedWireTypeException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="UnsupportedWireTypeException"/> 类的新实例。
        /// </summary>
        public UnsupportedWireTypeException()
        {
        }

        /// <summary>
        /// 初始化 <see cref="UnsupportedWireTypeException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的电文。</param>
        public UnsupportedWireTypeException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="UnsupportedWireTypeException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private UnsupportedWireTypeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 找不到引用值 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class ReferenceNotFoundException : SerializerException
    {
        /// <summary>
        /// 获得目标参照。
        /// </summary>
        /// <value>目标基准。</value>
        [Id(0)]
        public uint TargetReference { get; }

        /// <summary>
        /// 获取目标参考的类型。
        /// </summary>
        /// <value>目标参考类型。</value>
        [Id(1)]
        public Type? TargetReferenceType { get; }

        /// <summary>
        /// 初始化 <see cref="ReferenceNotFoundException"/> 类的新实例。
        /// </summary>
        /// <param name="targetType">目标类型。</param>
        /// <param name="targetId">目标识别符号。</param>
        public ReferenceNotFoundException(Type? targetType, uint targetId) : base(
            $"Reference with id {targetId} and type {targetType} not found.")
        {
            TargetReference = targetId;
            TargetReferenceType = targetType;
        }

        /// <summary>
        /// 初始化 <see cref="ReferenceNotFoundException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private ReferenceNotFoundException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            TargetReference = info.GetUInt32(nameof(TargetReference));
            TargetReferenceType = (Type)info.GetValue(nameof(TargetReferenceType), typeof(Type))!;
        }

        /// <inheritdoc/>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(TargetReference), TargetReference);
            info.AddValue(nameof(TargetReferenceType), TargetReferenceType);
        }
    }

    /// <summary>
    /// 找不到引用类型 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    [SuppressMessage("Design", "CA1032:Implement standard exception constructors",
        Justification = "The referenced type id is required error context and is preserved by the typed constructor and serialization contract.")]
    public sealed class UnknownReferencedTypeException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="UnknownReferencedTypeException"/> 类的新实例。
        /// </summary>
        /// <param name="reference">参考。</param>
        public UnknownReferencedTypeException(uint reference) : base($"Unknown referenced type {reference}.")
        {
            Reference = reference;
        }

        /// <summary>
        /// 初始化 <see cref="UnknownReferencedTypeException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private UnknownReferencedTypeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            Reference = info.GetUInt32(nameof(Reference));
        }

        /// <summary>
        /// 查找或设置引用。
        /// </summary>
        /// <value>参考。</value>
        [Id(0)]
        public uint Reference { get; set; }

        /// <inheritdoc/>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(Reference), Reference);
        }
    }

    /// <summary>
    /// 在此不支持引用值 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class ReferenceFieldNotSupportedException : SerializerException
    {
        /// <summary>
        /// 获取目标参考的类型。
        /// </summary>
        /// <value>目标参考类型。</value>
        [Id(0)]
        public Type? TargetReferenceType { get; }

        /// <summary>
        /// 初始化 <see cref="ReferenceFieldNotSupportedException"/> 类的新实例。
        /// </summary>
        /// <param name="targetType">目标类型。</param>
        public ReferenceFieldNotSupportedException(Type? targetType) : base(
            $"Reference with type {targetType} not allowed here.")
        {
            TargetReferenceType = targetType;
        }

        /// <summary>
        /// 初始化 <see cref="ReferenceFieldNotSupportedException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private ReferenceFieldNotSupportedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            TargetReferenceType = (Type)info.GetValue(nameof(TargetReferenceType), typeof(Type))!;
        }

        /// <inheritdoc/>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(TargetReferenceType), TargetReferenceType);
        }
    }

    /// <summary>
    /// 一种众所周知的类型尚不得而知。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    [SuppressMessage("Design", "CA1032:Implement standard exception constructors",
        Justification = "The well-known type id is required error context and is preserved by the typed constructor and serialization contract.")]
    public sealed class UnknownWellKnownTypeException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="UnknownWellKnownTypeException"/> 类的新实例。
        /// </summary>
        /// <param name="id">识别符号。</param>
        public UnknownWellKnownTypeException(uint id) : base($"Unknown well-known type {id}.")
        {
            Id = id;
        }

        /// <summary>
        /// 初始化 <see cref="UnknownWellKnownTypeException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private UnknownWellKnownTypeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            Id = info.GetUInt32(nameof(Id));
        }

        /// <summary>
        /// 获取或设置标识符。
        /// </summary>
        /// <value>识别符号。</value>
        [Id(0)]
        public uint Id { get; set; }

        /// <inheritdoc />
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(Id), Id);
        }
    }

    /// <summary>
    /// 不允许指定类型 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class IllegalTypeException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="IllegalTypeException"/> 类的新实例。
        /// </summary>
        /// <param name="typeName">类型的名称。</param>
        public IllegalTypeException(string typeName) : base($"Type \"{typeName}\" is not allowed.")
        {
            TypeName = typeName;
        }

        /// <summary>
        /// 初始化 <see cref="IllegalTypeException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private IllegalTypeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            TypeName = info.GetString(nameof(TypeName))!;
        }

        /// <summary>
        /// 获取类型的名称 。
        /// </summary>
        /// <value>类型的名称。</value>
        [Id(0)]
        public string TypeName { get; }

        /// <inheritdoc/>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(TypeName), TypeName);
        }
    }

    /// <summary>
    /// 期待有型号, 但找不到 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class TypeMissingException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="TypeMissingException"/> 类的新实例。
        /// </summary>
        public TypeMissingException() : base("Expected a type but none were encountered.")
        {
        }

        /// <summary>
        /// 初始化 <see cref="TypeMissingException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private TypeMissingException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 不存在所需的字段。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class RequiredFieldMissingException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="RequiredFieldMissingException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的电文。</param>
        public RequiredFieldMissingException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="RequiredFieldMissingException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private RequiredFieldMissingException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 找不到适合指定类型序列器编码器的合适代号 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class CodecNotFoundException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="CodecNotFoundException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的电文。</param>
        public CodecNotFoundException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="CodecNotFoundException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private CodecNotFoundException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 长度编码字段,其长度预计为
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class UnexpectedLengthPrefixValueException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="UnexpectedLengthPrefixValueException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的电文。</param>
        public UnexpectedLengthPrefixValueException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="UnexpectedLengthPrefixValueException"/> 类的新实例。
        /// </summary>
        /// <param name="typeName">类型的名称。</param>
        /// <param name="expectedLength">预期长度。</param>
        /// <param name="actualLength">实际长度。</param>
        public UnexpectedLengthPrefixValueException(string typeName, uint expectedLength, uint actualLength)
            : base($"VarInt length specified in header for {typeName} should be {expectedLength} but is instead {actualLength}")
        {
        }

        /// <summary>
        /// 初始化 <see cref="UnexpectedLengthPrefixValueException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private UnexpectedLengthPrefixValueException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
