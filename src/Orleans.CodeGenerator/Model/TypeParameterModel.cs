namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 描述可序列化类型或代理类型中的类型参数。
/// </summary>
internal readonly record struct TypeParameterModel(string Name, string OriginalName, int Ordinal);
