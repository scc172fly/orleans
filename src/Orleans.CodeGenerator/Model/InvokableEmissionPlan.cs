namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 生成的可调用对象声明的代码文本，在代理发现该可调用对象时规划。
/// </summary>
/// <remarks>
/// 可取消的可调用对象同时具有两个取消字段。<see cref="Arguments"/> 列出所有方法参数，包括取消令牌。
/// </remarks>
internal sealed record InvokableEmissionPlan(
    string ClassName,
    bool IsPublic,
    string BaseType,
    EquatableArray<string> CompoundTypeAliases,
    EquatableArray<GenericParameterEmissionPlan> TypeParameters,
    EquatableArray<string> Fields,
    InvokableConstructorEmissionPlan? Constructor,
    string? ResponseTimeoutTicks,
    string TimeSpanType,
    int ArgumentCount,
    string MethodName,
    string InterfaceName,
    string ActivityName,
    string SystemType,
    string InterfaceType,
    string MethodInfoType,
    string TargetHolderType,
    string TargetType,
    bool IsExtension,
    string? CancellationTokenSourceField,
    string? CancellationTokenField,
    string CancellationTokenType,
    EquatableArray<string> DisposeStatements,
    bool DisposeBase,
    EquatableArray<InvokableArgumentEmissionPlan> Arguments,
    string MaxArgumentIndex,
    string ReturnType,
    string InvokeTarget,
    string InvokeArguments,
    bool OptOutOfGrainCancellationTokenScan)
{
    /// <summary>生成类型在 <paramref name="generatedNamespace"/> 中的元数据名称。</summary>
    public string GetMetadataName(string generatedNamespace)
    {
        var name = string.IsNullOrEmpty(generatedNamespace) ? ClassName : $"{generatedNamespace}.{ClassName}";
        return TypeParameters.Count == 0 ? name : $"{name}`{TypeParameters.Count}";
    }
}

internal sealed record InvokableConstructorEmissionPlan(string Parameters, string BaseArguments, EquatableArray<string> Statements);

internal sealed record InvokableArgumentEmissionPlan(string Ordinal, string FieldName, string Type);
