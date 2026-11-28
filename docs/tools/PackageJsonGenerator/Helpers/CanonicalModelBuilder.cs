// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace PackageJsonGenerator.Helpers;

/// <summary>
/// 根据 Roslyn 符号构建规范化的 API 模型。
/// </summary>
internal sealed class CanonicalModelBuilder(Compilation compilation)
{
    private readonly Compilation _compilation = compilation;

    public List<CanonicalType> BuildTypes(ImmutableArray<INamedTypeSymbol> types)
    {
        var result = new List<CanonicalType>();

        foreach (var type in types.OrderBy(t => t.ToDisplayString()))
        {
            result.Add(BuildType(type));
        }

        return result;
    }

    private CanonicalType BuildType(INamedTypeSymbol symbol)
    {
        var isEnum = symbol.TypeKind == TypeKind.Enum;

        var type = new CanonicalType
        {
            Name = symbol.Name,
            FullName = symbol.ToDisplayString(),
            Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? "",
            Kind = CanonicalType.GetTypeKind(symbol),
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            IsAbstract = symbol.IsAbstract && symbol.TypeKind != TypeKind.Interface,
            IsSealed = symbol.IsSealed && !isEnum,
            IsStatic = symbol.IsStatic,
            IsGeneric = symbol.IsGenericType,
            IsReadOnly = symbol.IsReadOnly && symbol.IsValueType,
        };

        // 泛型参数
        if (symbol.IsGenericType)
        {
            type.GenericParameters = [.. symbol.TypeParameters.Select(BuildGenericParameter)];
        }

        // 基类型
        if (symbol.BaseType is not null &&
            symbol.BaseType.SpecialType != SpecialType.System_Object &&
            symbol.BaseType.SpecialType != SpecialType.System_ValueType &&
            symbol.BaseType.SpecialType != SpecialType.System_Enum)
        {
            type.BaseType = symbol.BaseType.ToDisplayString();
        }

        // 接口
        type.Interfaces = [.. symbol.Interfaces
            .Select(i => i.ToDisplayString())
            .OrderBy(i => i)];

        // 成员
        type.Members = BuildMembers(symbol);

        // XML 文档注释
        type.Docs = ExtractDocumentation(symbol);

        // 枚举成员
        if (isEnum)
        {
            type.EnumMembers = [.. symbol.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => f.HasConstantValue && !IsGenerated(f))
                .Select(f => new CanonicalEnumMember
                {
                    Name = f.Name,
                    Value = Convert.ToInt64(f.ConstantValue),
                    Description = ExtractSummary(f),
                })];
        }

        // 委托签名
        if (symbol.TypeKind == TypeKind.Delegate && symbol.DelegateInvokeMethod is { } invokeMethod)
        {
            type.DelegateReturnType = invokeMethod.ReturnsVoid ? "void" : invokeMethod.ReturnType.ToDisplayString();
            type.DelegateParameters = [.. invokeMethod.Parameters.Select(parameter => BuildParameter(parameter))];
        }

        // 特性
        type.Attributes = ExtractRelevantAttributes(symbol);

        // 嵌套类型：Roslyn 会合并分部声明，因此 GetTypeMembers()
        // 返回每个分部源文件贡献的嵌套类型（例如
        // FoundryModel + FoundryModel.Generated.cs）。
        var nestedNames = symbol.GetTypeMembers()
            .Where(IsPublicNestedType)
            .Select(t => t.ToDisplayString())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        if (nestedNames.Count > 0)
        {
            type.NestedTypes = nestedNames;
        }

        return type;
    }

    private static bool IsPublicNestedType(INamedTypeSymbol type)
    {
        return type.DeclaredAccessibility == Accessibility.Public
            && !string.IsNullOrEmpty(type.Name)
            && !IsGenerated(type);
    }

    internal static bool IsGenerated(ISymbol symbol) =>
        symbol.Name.StartsWith('<')
        || symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() is
            "System.Runtime.CompilerServices.CompilerGeneratedAttribute"
            or "System.CodeDom.Compiler.GeneratedCodeAttribute");

    private List<CanonicalMember> BuildMembers(INamedTypeSymbol type)
    {
        var members = new List<CanonicalMember>();

        foreach (var member in type.GetMembers().OrderBy(m => m.Name))
        {
            if (!IsExternallyVisible(member.DeclaredAccessibility) || member.IsImplicitlyDeclared || IsGenerated(member))
            {
                continue;
            }

            switch (member)
            {
                case IMethodSymbol method when method.MethodKind is
                    MethodKind.Ordinary or
                    MethodKind.Constructor or
                    MethodKind.UserDefinedOperator or
                    MethodKind.Conversion:
                    members.Add(BuildMethod(method));
                    break;

                case IPropertySymbol property:
                    members.Add(BuildProperty(property));
                    break;

                case IFieldSymbol field when type.TypeKind != TypeKind.Enum:
                    members.Add(BuildField(field));
                    break;

                case IEventSymbol evt:
                    members.Add(BuildEvent(evt));
                    break;
            }
        }

        return members;
    }

    private static bool IsExternallyVisible(Accessibility accessibility) =>
        accessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;

    private CanonicalMember BuildMethod(IMethodSymbol method) => new()
    {
        Name = method.MethodKind == MethodKind.Constructor ? ".ctor" : method.Name,
        Kind = method.MethodKind == MethodKind.Constructor ? "constructor" : "method",
        Accessibility = method.DeclaredAccessibility.ToString().ToLowerInvariant(),
        IsStatic = method.IsStatic,
        IsAbstract = method.IsAbstract,
        IsVirtual = method.IsVirtual,
        IsOverride = method.IsOverride,
        IsAsync = method.IsAsync,
        IsExtension = method.IsExtensionMethod,
        ReturnType = method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(),
        IsReturnNullable = method.ReturnType.NullableAnnotation == NullableAnnotation.Annotated,
        Parameters = [.. method.Parameters
            .AsEnumerable()
            .Select((parameter, index) => BuildParameter(parameter, index == 0 && method.IsExtensionMethod))],
        GenericParameters = method.IsGenericMethod
            ? [.. method.TypeParameters.Select(BuildGenericParameter)]
            : null,
        Signature = FormatMethodSignature(method),
        Docs = ExtractDocumentation(method),
        Attributes = ExtractRelevantAttributes(method),
    };

    private CanonicalMember BuildProperty(IPropertySymbol property) => new()
    {
        Name = property.IsIndexer ? "this[]" : property.Name,
        Kind = property.IsIndexer ? "indexer" : "property",
        Accessibility = property.DeclaredAccessibility.ToString().ToLowerInvariant(),
        IsStatic = property.IsStatic,
        IsAbstract = property.IsAbstract,
        IsVirtual = property.IsVirtual,
        IsOverride = property.IsOverride,
        HasGet = property.GetMethod is not null,
        HasSet = property.SetMethod is not null,
        IsInitOnly = property.SetMethod?.IsInitOnly ?? false,
        ReturnType = property.Type.ToDisplayString(),
        IsReturnNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated,
        Parameters = property.IsIndexer
            ? [.. property.Parameters.Select(parameter => BuildParameter(parameter))]
            : null,
        Signature = PrependModifiers(
            property.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            property.DeclaredAccessibility, property.IsStatic, property.IsAbstract,
            property.IsVirtual, property.IsOverride),
        Docs = ExtractDocumentation(property),
        Attributes = ExtractRelevantAttributes(property),
    };

    private static CanonicalParameter BuildParameter(IParameterSymbol parameter, bool isExtensionReceiver = false) => new()
    {
        Name = parameter.Name,
        Type = parameter.Type.ToDisplayString(),
        IsNullable = parameter.Type.NullableAnnotation == NullableAnnotation.Annotated,
        IsOptional = parameter.IsOptional,
        DefaultValue = parameter.HasExplicitDefaultValue ? parameter.ExplicitDefaultValue?.ToString() : null,
        Modifier = isExtensionReceiver
            ? "this"
            : parameter.RefKind switch
            {
                RefKind.Ref => "ref",
                RefKind.Out => "out",
                RefKind.In => "in",
                RefKind.RefReadOnlyParameter => "ref readonly",
                _ => parameter.IsParams ? "params" : null,
            },
        Attributes = ExtractRelevantAttributes(parameter),
    };

    private CanonicalMember BuildField(IFieldSymbol field) => new()
    {
        Name = field.Name,
        Kind = "field",
        Accessibility = field.DeclaredAccessibility.ToString().ToLowerInvariant(),
        IsStatic = field.IsStatic,
        IsConst = field.IsConst,
        IsReadOnly = field.IsReadOnly,
        ReturnType = field.Type.ToDisplayString(),
        IsReturnNullable = field.Type.NullableAnnotation == NullableAnnotation.Annotated,
        Signature = PrependModifiers(
            field.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            field.DeclaredAccessibility, field.IsStatic,
            isConst: field.IsConst, isReadOnly: field.IsReadOnly),
        Docs = ExtractDocumentation(field),
        Attributes = ExtractRelevantAttributes(field),
    };

    private CanonicalMember BuildEvent(IEventSymbol evt) => new()
    {
        Name = evt.Name,
        Kind = "event",
        Accessibility = evt.DeclaredAccessibility.ToString().ToLowerInvariant(),
        IsStatic = evt.IsStatic,
        IsAbstract = evt.IsAbstract,
        IsVirtual = evt.IsVirtual,
        IsOverride = evt.IsOverride,
        ReturnType = evt.Type.ToDisplayString(),
        Signature = PrependModifiers(
            evt.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            evt.DeclaredAccessibility, evt.IsStatic, evt.IsAbstract,
            evt.IsVirtual, evt.IsOverride),
        Docs = ExtractDocumentation(evt),
        Attributes = ExtractRelevantAttributes(evt),
    };

    /// <summary>
    /// 格式化方法签名，确保扩展方法的第一个参数带有 `this`，
    /// 因为 Roslyn 的 MinimallyQualifiedFormat 会将其省略。
    /// </summary>
    private static string FormatMethodSignature(IMethodSymbol method)
    {
        var sig = method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        if (method.IsExtensionMethod && method.Parameters.Length > 0)
        {
            // 签名形如：ReturnType Class.Method(ParamType paramName, ...)
            // 需要在括号内的第一个参数类型之前插入 "this "。
            var openParen = sig.IndexOf('(');
            if (openParen >= 0 && openParen + 1 < sig.Length && sig[openParen + 1] != ')')
            {
                sig = sig.Insert(openParen + 1, "this ");
            }
        }

        return PrependModifiers(sig, method.DeclaredAccessibility, method.IsStatic, method.IsAbstract,
            method.IsVirtual, method.IsOverride, isSealed: method.IsSealed && method.IsOverride,
            isAsync: method.IsAsync);
    }

    /// <summary>
    /// 通过在
    /// Roslyn 的
    /// <c>MinimallyQualifiedFormat</c> 字符串前添加 C# 风格的修饰符（可访问性、static 等），为属性、字段或事件构建便于显示的签名。
    /// </summary>
    private static string PrependModifiers(
        string rawSignature,
        Accessibility accessibility,
        bool isStatic = false,
        bool isAbstract = false,
        bool isVirtual = false,
        bool isOverride = false,
        bool isSealed = false,
        bool isConst = false,
        bool isReadOnly = false,
        bool isAsync = false)
    {
        var sb = new StringBuilder();

        // 可访问性
        var access = accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.Private => "private",
            _ => null,
        };
        if (access is not null)
        {
            sb.Append(access);
            sb.Append(' ');
        }

        // 按 C# 惯例排序的修饰符
        if (isConst) sb.Append("const ");
        if (isStatic && !isConst) sb.Append("static ");
        if (isAbstract) sb.Append("abstract ");
        if (isSealed) sb.Append("sealed ");
        if (isOverride) sb.Append("override ");
        else if (isVirtual) sb.Append("virtual ");
        if (isAsync) sb.Append("async ");
        if (isReadOnly && !isConst) sb.Append("readonly ");

        sb.Append(rawSignature);
        return sb.ToString();
    }

    private static CanonicalGenericParameter BuildGenericParameter(ITypeParameterSymbol tp) => new()
    {
        Name = tp.Name,
        Constraints = GetConstraints(tp),
    };

    private static List<string> GetConstraints(ITypeParameterSymbol tp)
    {
        var constraints = new List<string>();

        if (tp.HasReferenceTypeConstraint) constraints.Add("class");
        if (tp.HasValueTypeConstraint) constraints.Add("struct");
        if (tp.HasUnmanagedTypeConstraint) constraints.Add("unmanaged");
        if (tp.HasNotNullConstraint) constraints.Add("notnull");
        if (tp.HasConstructorConstraint) constraints.Add("new()");

        foreach (var ct in tp.ConstraintTypes)
        {
            constraints.Add(ct.ToDisplayString());
        }

        return constraints;
    }

    private CanonicalDocumentation? ExtractDocumentation(ISymbol symbol, int inheritDepth = 0)
    {
        var xmlComment = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xmlComment))
        {
            return inheritDepth < 3 ? TryResolveInheritDoc(symbol, null, inheritDepth) : null;
        }

        try
        {
            var doc = XDocument.Parse(xmlComment);
            var root = doc.Root;
            if (root is null) return null;

            // 检查是否有 <inheritdoc/>，支持可选的 cref 属性。
            var inheritDocElement = root.Element("inheritdoc");
            CanonicalDocumentation? inherited = null;
            if (inheritDocElement is not null && inheritDepth < 3)
            {
                var cref = inheritDocElement.Attribute("cref")?.Value;
                inherited = TryResolveInheritDoc(symbol, cref, inheritDepth);

                // 如果文档仅包含 <inheritdoc/>（没有其他章节），直接返回继承的文档。
                var hasExplicitContent = root.Element("summary") is not null ||
                    root.Element("remarks") is not null ||
                    root.Element("returns") is not null ||
                    root.Element("value") is not null ||
                    root.Elements("param").Any() ||
                    root.Elements("typeparam").Any() ||
                    root.Elements("exception").Any() ||
                    root.Elements("example").Any() ||
                    root.Elements("seealso").Any();

                if (!hasExplicitContent && inherited is not null)
                {
                    return inherited;
                }
            }

            var docs = new CanonicalDocumentation
            {
                Summary = ExtractDocNodes(root.Element("summary")),
                Remarks = ExtractDocNodes(root.Element("remarks")),
                Returns = ExtractDocNodes(root.Element("returns")),
                Value = ExtractDocNodes(root.Element("value")),
            };

            // 参数
            var paramElements = root.Elements("param").ToList();
            if (paramElements.Count > 0)
            {
                docs.Parameters = new Dictionary<string, List<DocNode>>();
                foreach (var param in paramElements)
                {
                    var name = param.Attribute("name")?.Value;
                    if (name is not null)
                    {
                        var nodes = ExtractDocNodes(param);
                        if (nodes is not null)
                        {
                            docs.Parameters[name] = nodes;
                        }
                    }
                }
                if (docs.Parameters.Count == 0) docs.Parameters = null;
            }

            // 类型参数
            var typeParamElements = root.Elements("typeparam").ToList();
            if (typeParamElements.Count > 0)
            {
                docs.TypeParameters = new Dictionary<string, List<DocNode>>();
                foreach (var tp in typeParamElements)
                {
                    var name = tp.Attribute("name")?.Value;
                    if (name is not null)
                    {
                        var nodes = ExtractDocNodes(tp);
                        if (nodes is not null)
                        {
                            docs.TypeParameters[name] = nodes;
                        }
                    }
                }
                if (docs.TypeParameters.Count == 0) docs.TypeParameters = null;
            }

            // 异常
            var exceptionElements = root.Elements("exception").ToList();
            if (exceptionElements.Count > 0)
            {
                docs.Exceptions = [];
                foreach (var exc in exceptionElements)
                {
                    var cref = exc.Attribute("cref")?.Value;
                    if (cref is not null)
                    {
                        docs.Exceptions.Add(new CanonicalException
                        {
                            Type = cref,
                            Description = ExtractDocNodes(exc),
                        });
                    }
                }
            }

            // 示例
            var exampleElements = root.Elements("example").ToList();
            if (exampleElements.Count > 0)
            {
                docs.Examples = exampleElements
                    .Select(ExtractDocExample)
                    .Where(e => e is not null)
                    .Cast<DocExample>()
                    .ToList();
                if (docs.Examples.Count == 0) docs.Examples = null;
            }

            // 另请参阅
            var seeAlsoElements = root.Elements("seealso").ToList();
            if (seeAlsoElements.Count > 0)
            {
                docs.SeeAlso = seeAlsoElements
                    .Select(s => s.Attribute("cref")?.Value)
                    .Where(c => c is not null)
                    .Cast<string>()
                    .ToList();
            }

            // 合并：从继承的文档中补齐缺失章节。
            if (inherited is not null)
            {
                MergeInheritedDocs(docs, inherited);
            }

            return docs;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 补齐 <paramref name="target"/> 中缺失的文档章节，内容来自
    /// <paramref name="inherited"/>。显式指定的章节绝不会被覆盖。
    /// </summary>
    private static void MergeInheritedDocs(CanonicalDocumentation target, CanonicalDocumentation inherited)
    {
        target.Summary ??= inherited.Summary;
        target.Remarks ??= inherited.Remarks;
        target.Returns ??= inherited.Returns;
        target.Value ??= inherited.Value;
        target.Examples ??= inherited.Examples;
        target.SeeAlso ??= inherited.SeeAlso;

        // 合并参数文档，仅补齐缺失的参数说明。
        if (inherited.Parameters is not null)
        {
            target.Parameters ??= new Dictionary<string, List<DocNode>>();
            foreach (var kvp in inherited.Parameters)
            {
                target.Parameters.TryAdd(kvp.Key, kvp.Value);
            }
        }

        // 合并类型参数文档。
        if (inherited.TypeParameters is not null)
        {
            target.TypeParameters ??= new Dictionary<string, List<DocNode>>();
            foreach (var kvp in inherited.TypeParameters)
            {
                target.TypeParameters.TryAdd(kvp.Key, kvp.Value);
            }
        }

        // 合并异常说明，仅追加尚未出现的异常类型。
        if (inherited.Exceptions is { Count: > 0 })
        {
            if (target.Exceptions is null)
            {
                target.Exceptions = inherited.Exceptions;
            }
            else
            {
                var existingTypes = new HashSet<string>(target.Exceptions.Select(e => e.Type));
                foreach (var exc in inherited.Exceptions)
                {
                    if (!existingTypes.Contains(exc.Type))
                    {
                        target.Exceptions.Add(exc);
                    }
                }
            }
        }
    }

    private CanonicalDocumentation? TryResolveInheritDoc(ISymbol symbol, string? cref, int inheritDepth)
    {
        ISymbol? baseSymbol;

        if (cref is not null)
        {
            baseSymbol = ResolveCrefSymbol(symbol, cref);
        }
        else
        {
            baseSymbol = FindInheritDocSource(symbol);
        }

        var documentation = baseSymbol is not null ? ExtractDocumentation(baseSymbol, inheritDepth + 1) : null;
        if (baseSymbol is not null && documentation is not null)
        {
            RemapInheritedDocumentation(symbol, baseSymbol, documentation);
        }

        return documentation;
    }

    /// <summary>
    /// 使用编译信息将文档中的 cref 字符串（例如 "T:Namespace.Type" 或
    /// "M:Namespace.Type.Method"）解析为 Roslyn 符号。
    /// </summary>
    private ISymbol? ResolveCrefSymbol(ISymbol context, string cref)
    {
        var resolvedSymbol = DocumentationCommentId.GetFirstSymbolForDeclarationId(cref, _compilation)
            ?? DocumentationCommentId.GetFirstSymbolForReferenceId(cref, _compilation);
        if (resolvedSymbol is not null)
        {
            return resolvedSymbol;
        }

        // 去除文档 ID 前缀（T:、M:、P:、F:、E:）。
        var docId = cref;
        if (docId.Length > 2 && docId[1] == ':')
        {
            docId = docId[2..];
        }

        // 去除方法参数，以便查找类型。
        var parenIdx = docId.IndexOf('(');
        var nameWithoutParams = parenIdx >= 0 ? docId[..parenIdx] : docId;

        // 先尝试按类型查找。
        var typeSymbol = _compilation.GetTypeByMetadataName(nameWithoutParams);
        if (typeSymbol is not null)
        {
            return typeSymbol;
        }

        // 尝试按成员查找：在最后一个点号处分割，得到所属类型和成员名称。
        var lastDot = nameWithoutParams.LastIndexOf('.');
        if (lastDot > 0)
        {
            var containingTypeName = nameWithoutParams[..lastDot];
            var memberName = nameWithoutParams[(lastDot + 1)..];

            // 处理泛型元数（例如 List`1）。
            var backtickIdx = containingTypeName.IndexOf('`');
            var metadataName = backtickIdx >= 0 ? containingTypeName : containingTypeName;

            var containingType = _compilation.GetTypeByMetadataName(metadataName);
            if (containingType is not null)
            {
                // 根据名称查找成员。
                var members = containingType.GetMembers(memberName);
                if (members.Length > 0)
                {
                    return members[0];
                }
            }
        }

        // 回退：尝试默认继承链。
        return FindInheritDocSource(context);
    }

    private static void RemapInheritedDocumentation(
        ISymbol targetSymbol,
        ISymbol sourceSymbol,
        CanonicalDocumentation documentation)
    {
        var targetParameters = GetParameters(targetSymbol);
        var sourceParameters = GetParameters(sourceSymbol);
        documentation.Parameters = RemapDocumentationByOrdinal(
            documentation.Parameters,
            sourceParameters.Select(static parameter => parameter.Name),
            targetParameters.Select(static parameter => parameter.Name));

        var targetTypeParameters = GetTypeParameters(targetSymbol);
        var sourceTypeParameters = GetTypeParameters(sourceSymbol);
        documentation.TypeParameters = RemapDocumentationByOrdinal(
            documentation.TypeParameters,
            sourceTypeParameters.Select(static parameter => parameter.Name),
            targetTypeParameters.Select(static parameter => parameter.Name));
    }

    private static Dictionary<string, List<DocNode>>? RemapDocumentationByOrdinal(
        Dictionary<string, List<DocNode>>? documentation,
        IEnumerable<string> sourceNames,
        IEnumerable<string> targetNames)
    {
        if (documentation is null)
        {
            return null;
        }

        var result = new Dictionary<string, List<DocNode>>();
        foreach (var pair in sourceNames.Zip(targetNames))
        {
            if (documentation.TryGetValue(pair.First, out var value))
            {
                result[pair.Second] = value;
            }
        }

        return result.Count > 0 ? result : null;
    }

    private static ImmutableArray<IParameterSymbol> GetParameters(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => method.Parameters,
        IPropertySymbol property => property.Parameters,
        _ => [],
    };

    private static ImmutableArray<ITypeParameterSymbol> GetTypeParameters(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => method.TypeParameters,
        INamedTypeSymbol type => type.TypeParameters,
        _ => [],
    };

    private static ISymbol? FindInheritDocSource(ISymbol symbol)
    {
        switch (symbol)
        {
            case IMethodSymbol method:
                if (method.OverriddenMethod is not null)
                    return method.OverriddenMethod;
                return FindInterfaceMemberImplementation(method);

            case IPropertySymbol property:
                if (property.OverriddenProperty is not null)
                    return property.OverriddenProperty;
                return FindInterfaceMemberImplementation(property);

            case IEventSymbol evt:
                if (evt.OverriddenEvent is not null)
                    return evt.OverriddenEvent;
                return FindInterfaceMemberImplementation(evt);

            case IFieldSymbol field:
                // 字段不能重写，但可能实现接口成员。
                return FindInterfaceMemberImplementation(field);

            case INamedTypeSymbol type:
                return type.BaseType;

            default:
                return null;
        }
    }

    private static ISymbol? FindInterfaceMemberImplementation(ISymbol member)
    {
        var containingType = member.ContainingType;
        if (containingType is null) return null;

        foreach (var iface in containingType.AllInterfaces)
        {
            foreach (var ifaceMember in iface.GetMembers())
            {
                var impl = containingType.FindImplementationForInterfaceMember(ifaceMember);
                if (impl is not null && SymbolEqualityComparer.Default.Equals(impl, member))
                {
                    return ifaceMember;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 从 XML 元素提取结构化文档节点，
    /// 保留语义结构，以便前端进行富文本渲染。
    /// </summary>
    private static List<DocNode>? ExtractDocNodes(XElement? element)
    {
        if (element is null) return null;

        var nodes = CollectNodes(element);
        return nodes.Count > 0 ? nodes : null;
    }

    /// <summary>
    /// 递归收集 XML 元素的子节点，生成由
    /// 结构化 DocNode 对象组成的扁平列表，同时保留行内与块级语义。
    /// </summary>
    private static List<DocNode> CollectNodes(XElement element)
    {
        var nodes = new List<DocNode>();

        foreach (var xNode in element.Nodes())
        {
            switch (xNode)
            {
                case XText textNode:
                    var text = textNode.Value;
                    // 规范化连续文本中的空白字符（将换行替换为空格，合并连续空白）。
                    text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                    while (text.Contains("  ")) text = text.Replace("  ", " ");
                    if (!string.IsNullOrEmpty(text))
                    {
                        nodes.Add(new DocNode { Kind = "text", Text = text });
                    }
                    break;

                case XElement child:
                    switch (child.Name.LocalName)
                    {
                        case "see":
                        {
                            var cref = child.Attribute("cref")?.Value;
                            if (cref is not null)
                            {
                                nodes.Add(new DocNode { Kind = "cref", Value = cref });
                            }
                            else
                            {
                                var langword = child.Attribute("langword")?.Value;
                                if (langword is not null)
                                {
                                    nodes.Add(new DocNode { Kind = "langword", Value = langword });
                                }
                                else
                                {
                                    var href = child.Attribute("href")?.Value;
                                    if (href is not null)
                                    {
                                        nodes.Add(new DocNode
                                        {
                                            Kind = "href",
                                            Value = href,
                                            Text = child.Value.Trim() is { Length: > 0 } t ? t : null,
                                        });
                                    }
                                }
                            }
                            break;
                        }

                        case "paramref":
                        {
                            var name = child.Attribute("name")?.Value;
                            if (name is not null)
                            {
                                nodes.Add(new DocNode { Kind = "paramref", Value = name });
                            }
                            break;
                        }

                        case "typeparamref":
                        {
                            var name = child.Attribute("name")?.Value;
                            if (name is not null)
                            {
                                nodes.Add(new DocNode { Kind = "typeparamref", Value = name });
                            }
                            break;
                        }

                        case "c":
                            nodes.Add(new DocNode { Kind = "code", Text = child.Value });
                            break;

                        case "code":
                        {
                            var lang = NormalizeLanguage(
                                child.Attribute("lang")?.Value
                                ?? child.Attribute("language")?.Value
                                ?? "csharp");
                            var region = child.Attribute("region")?.Value;
                            nodes.Add(new DocNode
                            {
                                Kind = "codeblock",
                                Text = child.Value,
                                Language = lang,
                                Value = region, // 复用 Value 属性来存储 region。
                            });
                            break;
                        }

                        case "para":
                        {
                            var children = CollectNodes(child);
                            if (children.Count > 0)
                            {
                                nodes.Add(new DocNode { Kind = "para", Children = children });
                            }
                            break;
                        }

                        case "note":
                        {
                            var noteType = child.Attribute("type")?.Value ?? "note";
                            var children = CollectNodes(child);
                            if (children.Count > 0)
                            {
                                nodes.Add(new DocNode { Kind = "note", Value = noteType, Children = children });
                            }
                            break;
                        }

                        case "list":
                        {
                            var style = child.Attribute("type")?.Value ?? "bullet";
                            var listNode = new DocNode { Kind = "list", Style = style };

                            // 标题
                            var headerElem = child.Element("listheader");
                            if (headerElem is not null)
                            {
                                listNode.Header = BuildDocListItem(headerElem);
                            }

                            // 条目
                            var items = child.Elements("item").ToList();
                            if (items.Count > 0)
                            {
                                listNode.Items = items.Select(BuildDocListItem).ToList();
                            }

                            nodes.Add(listNode);
                            break;
                        }

                        case "a":
                        {
                            var href = child.Attribute("href")?.Value;
                            if (href is not null)
                            {
                                nodes.Add(new DocNode
                                {
                                    Kind = "href",
                                    Value = href,
                                    Text = child.Value.Trim() is { Length: > 0 } t ? t : null,
                                });
                            }
                            break;
                        }

                        case "example":
                            // 收集嵌套在其他元素中的示例，作为子元素。
                            var exChildren = CollectNodes(child);
                            if (exChildren.Count > 0)
                            {
                                nodes.AddRange(exChildren);
                            }
                            break;

                        default:
                            // 未知元素：递归处理其子元素。
                            var defaultChildren = CollectNodes(child);
                            nodes.AddRange(defaultChildren);
                            break;
                    }
                    break;
            }
        }

        return nodes;
    }

    private static DocListItem BuildDocListItem(XElement element)
    {
        var term = ExtractDocNodes(element.Element("term"));
        var description = ExtractDocNodes(element.Element("description"));

        // 支持 XML 文档列表项中的纯文本或段落内容，
        // 即直接包含在 <item> 内，而不是嵌套的 <description> 节点内。
        if (term is null && description is null)
        {
            description = ExtractDocNodes(element);
        }

        return new DocListItem
        {
            Term = term,
            Description = description,
        };
    }

    /// <summary>
    /// 从 &lt;example&gt; XML 元素中提取结构化示例。
    /// 将代码块与说明文字分离。
    /// </summary>
    private static DocExample? ExtractDocExample(XElement exampleElement)
    {
        // 查找第一个 <code> 元素，它就是示例代码。
        var codeElement = exampleElement.Descendants("code").FirstOrDefault();
        if (codeElement is null)
        {
            // 无代码块时，尝试将全部内容用作代码。
            var plainText = ExtractPlainText(exampleElement);
            return plainText is not null ? new DocExample { Code = plainText } : null;
        }

        var lang = NormalizeLanguage(
            codeElement.Attribute("lang")?.Value
            ?? codeElement.Attribute("language")?.Value
            ?? "csharp");
        var region = codeElement.Attribute("region")?.Value;

        // 收集说明节点：即不属于代码元素的所有内容。
        var descNodes = new List<DocNode>();
        foreach (var node in exampleElement.Nodes())
        {
            if (node == codeElement) continue;

            switch (node)
            {
                case XText textNode:
                    var text = textNode.Value.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                    while (text.Contains("  ")) text = text.Replace("  ", " ");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        descNodes.Add(new DocNode { Kind = "text", Text = text.Trim() });
                    }
                    break;

                case XElement child when child != codeElement:
                    var childNodes = CollectNodes(child);
                    if (child.Name.LocalName == "para")
                    {
                        if (childNodes.Count > 0)
                            descNodes.Add(new DocNode { Kind = "para", Children = childNodes });
                    }
                    else
                    {
                        descNodes.AddRange(childNodes);
                    }
                    break;
            }
        }

        return new DocExample
        {
            Code = codeElement.Value,
            Language = lang,
            Description = descNodes.Count > 0 ? descNodes : null,
            Region = region,
        };
    }

    /// <summary>
    /// 从 XML 元素提取纯文本，并将所有结构扁平化。
    /// 用于枚举成员说明等简单场景。
    /// </summary>
    private static string? ExtractPlainText(XElement? element)
    {
        if (element is null) return null;

        var sb = new StringBuilder();
        AppendPlainText(element, sb);

        var text = sb.ToString()
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Trim();

        // 将连续空格合并为一个空格。
        while (text.Contains("  "))
        {
            text = text.Replace("  ", " ");
        }

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static void AppendPlainText(XElement element, StringBuilder sb)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText textNode:
                    sb.Append(textNode.Value);
                    break;

                case XElement child:
                    switch (child.Name.LocalName)
                    {
                        case "see":
                            var cref = child.Attribute("cref")?.Value;
                            if (cref is not null)
                            {
                                sb.Append(ExtractShortNameFromCref(cref));
                            }
                            else
                            {
                                var langword = child.Attribute("langword")?.Value;
                                if (langword is not null)
                                {
                                    sb.Append(langword);
                                }
                            }
                            break;

                        case "paramref":
                        case "typeparamref":
                            var name = child.Attribute("name")?.Value;
                            if (name is not null)
                            {
                                sb.Append(name);
                            }
                            break;

                        case "c":
                        case "code":
                            sb.Append(child.Value);
                            break;

                        case "para":
                            sb.Append(' ');
                            AppendPlainText(child, sb);
                            sb.Append(' ');
                            break;

                        default:
                            AppendPlainText(child, sb);
                            break;
                    }
                    break;
            }
        }
    }

    private static string ExtractShortNameFromCref(string cref)
    {
        // 去除文档 ID 前缀（T:、M:、P:、F:、E:、N:）。
        if (cref.Length > 2 && cref[1] == ':')
        {
            cref = cref[2..];
        }

        // 去除方法参数。
        var parenIdx = cref.IndexOf('(');
        if (parenIdx >= 0)
        {
            cref = cref[..parenIdx];
        }

        // 获取最后一段（类型或成员名称）。
        var lastDot = cref.LastIndexOf('.');
        if (lastDot >= 0)
        {
            cref = cref[(lastDot + 1)..];
        }

        // 去除泛型元数后缀（例如 List`1 → List）。
        var backtickIdx = cref.IndexOf('`');
        if (backtickIdx >= 0)
        {
            cref = cref[..backtickIdx];
        }

        return cref;
    }

    private static string? ExtractSummary(ISymbol symbol)
    {
        var xmlComment = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xmlComment)) return null;

        try
        {
            var doc = XDocument.Parse(xmlComment);
            return ExtractPlainText(doc.Root?.Element("summary"));
        }
        catch
        {
            return null;
        }
    }

    private static List<CanonicalAttribute>? ExtractRelevantAttributes(ISymbol symbol)
    {
        var relevant = symbol.GetAttributes()
            .Where(a => IsRelevantAttribute(a.AttributeClass?.Name ?? ""))
            .Select(a => new CanonicalAttribute
            {
                Name = a.AttributeClass?.ToDisplayString() ?? "",
                ConstructorArguments = ExtractConstructorArguments(a),
                Arguments = ExtractNamedArguments(a),
            })
            .ToList();

        return relevant.Count > 0 ? relevant : null;
    }

    private static bool IsRelevantAttribute(string name) => name is
        "AliasAttribute" or
        "AlwaysInterleaveAttribute" or
        "DefaultGrainTypeAttribute" or
        "GenerateMethodSerializersAttribute" or
        "GenerateSerializerAttribute" or
        "GrainDirectoryAttribute" or
        "GrainInterfaceTypeAttribute" or
        "GrainTypeAttribute" or
        "IdAttribute" or
        "ImmutableAttribute" or
        "LogConsistencyProviderAttribute" or
        "MayInterleaveAttribute" or
        "OneWayAttribute" or
        "ReadOnlyAttribute" or
        "ReentrantAttribute" or
        "StatelessWorkerAttribute" or
        "StorageProviderAttribute" or
        "SuppressReferenceTrackingAttribute" or
        "TransactionAttribute" or
        "ObsoleteAttribute" or
        "ExperimentalAttribute" or
        "FlagsAttribute" or
        "JsonConverterAttribute" or
        "JsonDerivedTypeAttribute" or
        "JsonPolymorphicAttribute" or
        "JsonNumberHandlingAttribute" or
        "JsonRequiredAttribute" or
        "RequiredAttribute" or
        "DefaultValueAttribute";

    private static List<string>? ExtractConstructorArguments(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length == 0)
        {
            return null;
        }

        var values = attribute.ConstructorArguments
            .SelectMany(FlattenTypedConstant)
            .ToList();

        return values.Count > 0 ? values : null;
    }

    private static Dictionary<string, string>? ExtractNamedArguments(AttributeData attribute)
    {
        if (attribute.NamedArguments.Length == 0)
        {
            return null;
        }

        var values = attribute.NamedArguments
            .ToDictionary(n => n.Key, n => FormatTypedConstant(n.Value));

        return values.Count > 0 ? values : null;
    }

    private static IEnumerable<string> FlattenTypedConstant(TypedConstant constant)
    {
        if (constant.Kind == TypedConstantKind.Array)
        {
            foreach (var item in constant.Values)
            {
                foreach (var nested in FlattenTypedConstant(item))
                {
                    yield return nested;
                }
            }

            yield break;
        }

        yield return FormatTypedConstant(constant);
    }

    private static string FormatTypedConstant(TypedConstant constant)
    {
        if (constant.Kind == TypedConstantKind.Array)
        {
            return string.Join(", ", constant.Values.SelectMany(FlattenTypedConstant));
        }

        return constant.Value switch
        {
            ITypeSymbol typeSymbol => typeSymbol.ToDisplayString(),
            null => "null",
            _ => constant.Value.ToString() ?? "",
        };
    }

    /// <summary>
    /// 将 XML 文档中的语言标识符规范化为
    /// 语法高亮器所需的小写别名（例如 "C#" → "csharp"）。
    /// </summary>
    private static string NormalizeLanguage(string lang) => lang switch
    {
        "C#" => "csharp",
        "F#" => "fsharp",
        "VB" or "VB.NET" => "vb",
        _ => lang.ToLowerInvariant(),
    };
}
