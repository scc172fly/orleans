namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 描述可序列或代理类型的类型参数。
/// </summary>
internal readonly record struct TypeParameterModel(string Name, string OriginalName, int Ordinal);
