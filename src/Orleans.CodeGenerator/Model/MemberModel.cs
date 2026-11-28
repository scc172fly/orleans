namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 描述可序列化成员的种类。
/// </summary>
internal enum MemberKind : byte
{
    Field,
    Property
}

/// <summary>
/// 描述序列化期间获取或设置成员值的访问策略。
/// </summary>
internal enum AccessStrategy : byte
{
    /// <summary>
    /// 可直接访问成员（公共字段，或具有可访问 getter/setter 的属性）。
    /// </summary>
    Direct,

    /// <summary>
    /// 成员需要生成的基于委托的访问器（FieldAccessor 实用工具）。
    /// </summary>
    GeneratedAccessor,

    /// <summary>
    /// 成员需要基于 UnsafeAccessor 的访问器。
    /// </summary>
    UnsafeAccessor
}

/// <summary>
/// 描述反序列化期间构造可序列化类型实例的策略。
/// </summary>
internal enum ObjectCreationStrategy : byte
{
    /// <summary>
    /// 对值类型使用 <c>default(T)</c>。
    /// </summary>
    Default,

    /// <summary>
    /// 使用 <c>new T()</c>：类型具有可访问的无参构造函数。
    /// </summary>
    NewExpression,

    /// <summary>
    /// 使用 <c>RuntimeHelpers.GetUninitializedObject(typeof(T))</c>。
    /// </summary>
    GetUninitializedObject
}

/// <summary>
/// 描述 <see cref="SerializableTypeModel"/> 中的可序列化字段或属性成员。
/// 包含生成序列化器、复制器及激活器所需的全部数据，不持有 <c>ISymbol</c> 引用。
/// </summary>
internal sealed record class MemberModel
{
    public MemberModel(
        uint fieldId,
        string name,
        TypeRef type,
        TypeRef containingType,
        string assemblyName,
        string typeNameIdentifier,
        bool isPrimaryConstructorParameter,
        bool isSerializable,
        bool isCopyable,
        MemberKind kind,
        AccessStrategy getterStrategy,
        AccessStrategy setterStrategy,
        bool isObsolete,
        bool hasImmutableAttribute,
        bool isShallowCopyable,
        bool isValueType,
        bool containingTypeIsValueType,
        string? backingPropertyName)
    {
        FieldId = fieldId;
        Name = name;
        Type = type;
        ContainingType = containingType;
        AssemblyName = assemblyName;
        TypeNameIdentifier = typeNameIdentifier;
        IsPrimaryConstructorParameter = isPrimaryConstructorParameter;
        IsSerializable = isSerializable;
        IsCopyable = isCopyable;
        Kind = kind;
        GetterStrategy = getterStrategy;
        SetterStrategy = setterStrategy;
        IsObsolete = isObsolete;
        HasImmutableAttribute = hasImmutableAttribute;
        IsShallowCopyable = isShallowCopyable;
        IsValueType = isValueType;
        ContainingTypeIsValueType = containingTypeIsValueType;
        BackingPropertyName = backingPropertyName;
    }

    public uint FieldId { get; }
    public string Name { get; }
    public TypeRef Type { get; }
    public TypeRef ContainingType { get; }
    public string AssemblyName { get; }
    public string TypeNameIdentifier { get; }
    public bool IsPrimaryConstructorParameter { get; }
    public bool IsSerializable { get; }
    public bool IsCopyable { get; }
    public MemberKind Kind { get; }
    public AccessStrategy GetterStrategy { get; }
    public AccessStrategy SetterStrategy { get; }
    public bool IsObsolete { get; }
    public bool HasImmutableAttribute { get; }
    public bool IsShallowCopyable { get; }
    public bool IsValueType { get; }
    public bool ContainingTypeIsValueType { get; }
    public string? BackingPropertyName { get; }

}
