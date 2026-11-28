using System;

namespace Orleans;

/// <summary>
/// 为生成的上下文声明一个封闭的可序列化类型及其依赖关系图。
/// </summary>
/// <typeparam name="T">要注册的封闭根类型。</typeparam>
/// <remarks>
/// 将此特性应用于派生自 <c>Orleans.Serialization.GeneratedContext</c> 的分部类。
/// 每个根类型都必须是上下文生成器所支持的封闭类型。注册生成的上下文时，
/// 使用 <c>AddGeneratedContext</c> 启用显式类型查找。
/// 使用 <see cref="GeneratedContextImportAttribute{TContext}"/> 包含由其他
/// 上下文提供的类型图。被包含的模型根类型使用其提供方的工厂，只需要引用元数据。
/// 使用外部模型及其生成的编解码器和复制器时，通过完整的实现
/// 元数据获取所需信息。这提供完整的依赖关系图，包括私有状态和专用
/// 访问器。其他使用方可以使用来自同一提供方的引用程序集。
/// 当生成的 C# 编解码器
/// 和复制器通过完整实现元数据提供时，F# 模型引用声明会提供所需的类型头信息。
/// 泛型特性要求 C# 11 或更高版本。
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class GeneratedContextTypeAttribute<T> : Attribute;
