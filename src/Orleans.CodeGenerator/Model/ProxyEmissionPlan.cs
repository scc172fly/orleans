namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 生成代用类的拼写声明。 每个构建者共享 <see cref="ConstructorStatements"/>。
/// </summary>
internal sealed record ProxyEmissionPlan(
    string ClassName,
    string BaseType,
    string InterfaceType,
    EquatableArray<GenericParameterEmissionPlan> TypeParameters,
    EquatableArray<string> Fields,
    EquatableArray<string> Constructors,
    EquatableArray<string> ConstructorStatements,
    EquatableArray<ProxyMethodEmissionPlan> Methods)
{
    /// <summary>生成类型在 <paramref name="generatedNamespace"/>.</summary>
    public string GetMetadataName(string generatedNamespace)
    {
        var name = string.IsNullOrEmpty(generatedNamespace) ? ClassName : $"{generatedNamespace}.{ClassName}";
        return TypeParameters.Count == 0 ? name : $"{name}`{TypeParameters.Count}";
    }
}

/// <summary>明确的接口执行:其申报项目和机构报表。</summary>
internal sealed record ProxyMethodEmissionPlan(string Declaration, EquatableArray<string> Statements);
