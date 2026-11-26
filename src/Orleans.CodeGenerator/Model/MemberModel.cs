namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 描述一个可以连号的成员的类型。
/// </summary>
internal enum MemberKind : byte
{
    Field,
    Property
}

/// <summary>
/// 描述在序列过程中获取/确定成员价值的无障碍战略。
/// </summary>
internal enum AccessStrategy : byte
{
    /// <summary>
    /// 成员可以直接进入(公共领域或有无障碍取用/改进的地产)。
    /// </summary>
    Direct,

    /// <summary>
    /// 成员要求生成一个以代表为基础的访问(FieldAccessor 有用)功能。
    /// </summary>
    GeneratedAccessor,

    /// <summary>
    /// 成员需要 UnsafeAccessor-基于存取器。
    /// </summary>
    UnsafeAccessor
}

/// <summary>
/// 描述在脱序过程中构建一个可连号类型的例子的战略。
/// </summary>
internal enum ObjectCreationStrategy : byte
{
    /// <summary>
    /// 使用 <c>default(T)</c> 来计算数值类型 。
    /// </summary>
    Default,

    /// <summary>
    /// 使用 <c>new T()</c> ——类型有一个无障碍无参数构建器。
    /// </summary>
    NewExpression,

    /// <summary>
    /// 使用 <c>RuntimeHelpers.GetUninitializedObject(typeof(T))</c>。
    /// </summary>
    GetUninitializedObject
}

/// <summary>
/// 描述 <see cref="SerializableTypeModel"/>中可连号作业场或财产成员。
/// 包含序列器、复印机和活性器生成所需的全部数据,不持有 <c>ISymbol</c> 参考文献。
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
