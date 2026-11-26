// 根据一项或多项协定获得 .NET 基金会许可证。
// 下 .NET 基金会根据麻省理工学院的许可证 将这个文件发给你

namespace PackageJsonGenerator.Helpers;

/// <summary>化学表示一种化学排放类型。</summary>
internal sealed class CanonicalType
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Accessibility { get; set; } = "public";
    public bool IsAbstract { get; set; }
    public bool IsSealed { get; set; }
    public bool IsStatic { get; set; }
    public bool IsGeneric { get; set; }
    public bool IsReadOnly { get; set; }
    public List<CanonicalGenericParameter>? GenericParameters { get; set; }
    public string? BaseType { get; set; }
    public List<string> Interfaces { get; set; } = [];
    public List<CanonicalMember> Members { get; set; } = [];
    public CanonicalDocumentation? Docs { get; set; }
    public List<CanonicalEnumMember>? EnumMembers { get; set; }
    public string? DelegateReturnType { get; set; }
    public List<CanonicalParameter>? DelegateParameters { get; set; }
    public List<CanonicalAttribute>? Attributes { get; set; }
    /// <summary>此类申报的公巢类型全名。Roslyn合并部分申报,包括每个部分源文件提供的嵌套类型。</summary>
    public List<string>? NestedTypes { get; set; }
    /// <summary>相对对应的源文件路径 (e.g. "src/Orleans.Core/Foo.cs" 。</summary>
    public string? SourceFile { get; set; }
    /// <summary>源文件( 源文件中的行区域) (e.g. "15-200").</summary>
    public string? SourceLines { get; set; }

    internal static string GetTypeKind(Microsoft.CodeAnalysis.INamedTypeSymbol symbol)
    {
        if (symbol.IsRecord) return symbol.IsValueType ? "record struct" : "record";

        return symbol.TypeKind switch
        {
            Microsoft.CodeAnalysis.TypeKind.Class => "class",
            Microsoft.CodeAnalysis.TypeKind.Struct => "struct",
            Microsoft.CodeAnalysis.TypeKind.Extension => "extension",
            Microsoft.CodeAnalysis.TypeKind.Interface => "interface",
            Microsoft.CodeAnalysis.TypeKind.Enum => "enum",
            Microsoft.CodeAnalysis.TypeKind.Delegate => "delegate",
            _ => "class",
        };
    }
}

/// <summary>克隆通用参数。</summary>
internal sealed class CanonicalGenericParameter
{
    public string Name { get; set; } = "";
    public List<string> Constraints { get; set; } = [];
}

/// <summary>堪称卡尼科成员</summary>
internal sealed class CanonicalMember
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Accessibility { get; set; } = "public";
    public bool IsStatic { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsOverride { get; set; }
    public bool IsAsync { get; set; }
    public bool IsExtension { get; set; }
    public bool HasGet { get; set; }
    public bool HasSet { get; set; }
    public bool IsInitOnly { get; set; }
    public bool IsConst { get; set; }
    public bool IsReadOnly { get; set; }
    public string? ReturnType { get; set; }
    public bool IsReturnNullable { get; set; }
    public List<CanonicalParameter>? Parameters { get; set; }
    public List<CanonicalGenericParameter>? GenericParameters { get; set; }
    public string Signature { get; set; } = "";
    public CanonicalDocumentation? Docs { get; set; }
    public List<CanonicalAttribute>? Attributes { get; set; }
    /// <summary>相对对应的源文件路径 (e.g. "src/Orleans.Core/Foo.cs" 。</summary>
    public string? SourceFile { get; set; }
    /// <summary>源文件( 源文件中的行区域) (e.g. "15-200").</summary>
    public string? SourceLines { get; set; }
}
internal sealed class CanonicalParameter
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsNullable { get; set; }
    public bool IsOptional { get; set; }
    public string? DefaultValue { get; set; }
    public string? Modifier { get; set; }
    public List<CanonicalAttribute>? Attributes { get; set; }
}

/// <summary>堪尼卡金库姆成员</summary>
internal sealed class CanonicalEnumMember
{
    public string Name { get; set; } = "";
    public long Value { get; set; }
    public string? Description { get; set; }
}

/// <summary>坚固的属性</summary>
internal sealed class CanonicalAttribute
{
    public string Name { get; set; } = "";
    public List<string>? ConstructorArguments { get; set; }
    public Dictionary<string, string>? Arguments { get; set; }
}

/// <summary>谷歌文件。</summary>
internal sealed class CanonicalDocumentation
{
    public List<DocNode>? Summary { get; set; }
    public List<DocNode>? Remarks { get; set; }
    public List<DocNode>? Returns { get; set; }
    public Dictionary<string, List<DocNode>>? Parameters { get; set; }
    public Dictionary<string, List<DocNode>>? TypeParameters { get; set; }
    public List<CanonicalException>? Exceptions { get; set; }
    public List<DocExample>? Examples { get; set; }
    public List<DocNode>? Value { get; set; }
    public List<string>? SeeAlso { get; set; }
}

/// <summary>成员可以提出有记录的例外。</summary>
internal sealed class CanonicalException
{
    public string Type { get; set; } = "";
    public List<DocNode>? Description { get; set; }
}

/// <summary>
/// 结构化文档节点, 保存 XML 文档元素语义。
/// 用于 DocContent.astro 组件所消耗的丰富格式的地图。
/// </summary>
internal sealed class DocNode
{
    public string Kind { get; set; } = "";
    public string? Text { get; set; }
    public string? Value { get; set; }
    public string? Language { get; set; }
    public string? Style { get; set; }
    public List<DocNode>? Children { get; set; }
    public List<DocListItem>? Items { get; set; }
    public DocListItem? Header { get; set; }
}

/// <summary>文件清单中的列表项目。</summary>
internal sealed class DocListItem
{
    public List<DocNode>? Term { get; set; }
    public List<DocNode>? Description { get; set; }
}

/// <summary>XML doc &lt;等&gt; 元素的结构性代码示例。</summary>
internal sealed class DocExample
{
    public string Code { get; set; } = "";
    public string Language { get; set; } = "csharp";
    public List<DocNode>? Description { get; set; }
    public string? Region { get; set; }
}
