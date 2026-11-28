namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 生成的代理类声明的代码文本。每个构造函数都共享 <see cref="ConstructorStatements"/>。
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
    /// <summary>生成类型在 <paramref name="generatedNamespace"/> 中的元数据名称。</summary>
    public string GetMetadataName(string generatedNamespace)
    {
        var name = string.IsNullOrEmpty(generatedNamespace) ? ClassName : $"{generatedNamespace}.{ClassName}";
        return TypeParameters.Count == 0 ? name : $"{name}`{TypeParameters.Count}";
    }
}

/// <summary>显式接口实现：声明行和方法体语句。</summary>
internal sealed record ProxyMethodEmissionPlan(string Declaration, EquatableArray<string> Statements);
