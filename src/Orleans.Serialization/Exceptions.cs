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
    /// 所有序列化器异常的基类。
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
        /// <param name="message">描述错误的消息。</param>
        public SerializerException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="SerializerException"/> 类的新实例。
        /// </summary>
        /// <param name="message">解释异常原因的错误消息。</param>
        /// <param name="innerException">导致当前异常的异常；若未指定内部异常，则为 null 引用（Visual Basic 中为 <see langword="Nothing" />）。</param>
        public SerializerException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="SerializerException"/> 类的新实例。
        /// </summary>
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        protected SerializerException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 预期存在字段标识符，但该标识符缺失。
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
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private FieldIdNotPresentException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 架构类型无效。
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
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private SchemaTypeInvalidException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 字段类型无效。
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
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private FieldTypeInvalidException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 预期存在字段类型，但该类型缺失。
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
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private FieldTypeMissingException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 序列化后的带标记联合值指向联合类型或封闭类型层次结构中未声明的分支。
    /// </summary>
    /// <remarks>
    /// 当较旧版本的类型读取由声明了额外分支的较新版本写入的数据时，会发生此异常。
    /// </remarks>
    [Serializable]
    [GenerateSerializer]
    public sealed class UnknownUnionCaseException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="UnknownUnionCaseException"/> 类的新实例。
        /// </summary>
        /// <param name="unionType">正在反序列化的联合类型或封闭基类型。</param>
        /// <param name="caseId">读取到的分支标识符。</param>
        public UnknownUnionCaseException(Type unionType, uint caseId) : base($"Encountered unknown case {caseId} while deserializing a value of type {unionType}.")
        {
        }

        /// <summary>
        /// 初始化 <see cref="UnknownUnionCaseException"/> 类的新实例。
        /// </summary>
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private UnknownUnionCaseException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 扩展传输格式类型无效。
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
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private ExtendedWireTypeInvalidException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 不支持该传输格式类型。
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
        /// <param name="message">描述错误的消息。</param>
        public UnsupportedWireTypeException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="UnsupportedWireTypeException"/> 类的新实例。
        /// </summary>
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private UnsupportedWireTypeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 未找到被引用的值。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class ReferenceNotFoundException : SerializerException
    {
        /// <summary>
        /// 获取目标引用。
        /// </summary>
        /// <value>目标引用。</value>
        [Id(0)]
        public uint TargetReference { get; }

        /// <summary>
        /// 获取目标引用的类型。
        /// </summary>
        /// <value>目标引用的类型。</value>
        [Id(1)]
        public Type? TargetReferenceType { get; }

        /// <summary>
        /// 初始化 <see cref="ReferenceNotFoundException"/> 类的新实例。
        /// </summary>
        /// <param name="targetType">目标类型。</param>
        /// <param name="targetId">目标标识符。</param>
        public ReferenceNotFoundException(Type? targetType, uint targetId) : base(
            $"Reference with id {targetId} and type {targetType} not found.")
        {
            TargetReference = targetId;
            TargetReferenceType = targetType;
        }

        /// <summary>
        /// 初始化 <see cref="ReferenceNotFoundException"/> 类的新实例。
        /// </summary>
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
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
    /// 未找到被引用的类型。
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
        /// <param name="reference">引用。</param>
        public UnknownReferencedTypeException(uint reference) : base($"Unknown referenced type {reference}.")
        {
            Reference = reference;
        }

        /// <summary>
        /// 初始化 <see cref="UnknownReferencedTypeException"/> 类的新实例。
        /// </summary>
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private UnknownReferencedTypeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            Reference = info.GetUInt32(nameof(Reference));
        }

        /// <summary>
        /// 获取或设置引用。
        /// </summary>
        /// <value>引用。</value>
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
    /// 此处不支持值引用。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class ReferenceFieldNotSupportedException : SerializerException
    {
        /// <summary>
        /// 获取目标引用的类型。
        /// </summary>
        /// <value>目标引用的类型。</value>
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
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
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
    /// 无法识别某个应为已知类型的类型。
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
        /// <param name="id">标识符。</param>
        public UnknownWellKnownTypeException(uint id) : base($"Unknown well-known type {id}.")
        {
            Id = id;
        }

        /// <summary>
        /// 初始化 <see cref="UnknownWellKnownTypeException"/> 类的新实例。
        /// </summary>
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
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
        /// <value>标识符。</value>
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
    /// 不允许使用指定的类型。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class IllegalTypeException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="IllegalTypeException"/> 类的新实例。
        /// </summary>
        /// <param name="typeName">类型名称。</param>
        public IllegalTypeException(string typeName) : base($"Type \"{typeName}\" is not allowed.")
        {
            TypeName = typeName;
        }

        /// <summary>
        /// 初始化 <see cref="IllegalTypeException"/> 类的新实例。
        /// </summary>
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private IllegalTypeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            TypeName = info.GetString(nameof(TypeName))!;
        }

        /// <summary>
        /// 获取类型名称。
        /// </summary>
        /// <value>类型名称。</value>
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
    /// 预期存在类型，但未找到。
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
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private TypeMissingException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 必需字段缺失。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class RequiredFieldMissingException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="RequiredFieldMissingException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的消息。</param>
        public RequiredFieldMissingException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="RequiredFieldMissingException"/> 类的新实例。
        /// </summary>
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private RequiredFieldMissingException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 未找到适用于指定类型的序列化编解码器。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class CodecNotFoundException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="CodecNotFoundException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的消息。</param>
        public CodecNotFoundException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="CodecNotFoundException"/> 类的新实例。
        /// </summary>
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private CodecNotFoundException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// 一个带长度编码的字段，其预期长度
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class UnexpectedLengthPrefixValueException : SerializerException
    {
        /// <summary>
        /// 初始化 <see cref="UnexpectedLengthPrefixValueException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的消息。</param>
        public UnexpectedLengthPrefixValueException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="UnexpectedLengthPrefixValueException"/> 类的新实例。
        /// </summary>
        /// <param name="typeName">类型名称。</param>
        /// <param name="expectedLength">预期长度。</param>
        /// <param name="actualLength">实际长度。</param>
        public UnexpectedLengthPrefixValueException(string typeName, uint expectedLength, uint actualLength)
            : base($"VarInt length specified in header for {typeName} should be {expectedLength} but is instead {actualLength}")
        {
        }

        /// <summary>
        /// 初始化 <see cref="UnexpectedLengthPrefixValueException"/> 类的新实例。
        /// </summary>
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        private UnexpectedLengthPrefixValueException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
