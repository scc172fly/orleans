using System;

namespace Orleans;

/// <summary>
/// 导入由另一个生成的上下文提供的封闭类型图。
/// </summary>
/// <typeparam name="TContext">要包含的具体生成上下文。</typeparam>
/// <remarks>
/// 将此特性应用于派生自 <c>Orleans.Serialization.GeneratedContext</c> 的分部类。
/// 被包含的上下文必须支持可访问的无参构造，并使用
/// <see cref="GeneratedContextTypeAttribute{T}"/> 声明其根类型，或者使用此特性声明链接。
/// 生成器将这些根类型视为已提供的端点，并遍历其余的本地类型图。
/// 被包含的上下文提供自己的工厂和依赖关系元数据，因此使用方可以使用
/// 引用程序集。包含关系形成无环图，确保每个根类型都有所属上下文。
/// 每个类型清单提供程序键只配置一次共享链接。
/// 使用 <c>AddGeneratedContext</c> 注册生成的上下文。
/// 泛型特性要求 C# 11 或更高版本。
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class GeneratedContextImportAttribute<TContext> : Attribute;
