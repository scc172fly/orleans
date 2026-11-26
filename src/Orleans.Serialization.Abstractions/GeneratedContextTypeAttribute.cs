using System;

namespace Orleans;

/// <summary>
/// 声明封闭型序列可连号类型及其生成上下文的依附图。
/// </summary>
/// <typeparam name="T">要登记的封闭根类型。</typeparam>
/// <remarks>
/// 将此属性应用到从 <c>Orleans.Serialization.GeneratedContext</c>衍生出来的部分类别。
/// 根必须由上下文生成器支持的封闭型, 根必须都是封闭型。 登记生成的上下文
/// 使用 <c>AddGeneratedContext</c> 进行清晰的打印搜索。
/// 使用 <see cref="GeneratedContextImportAttribute{TContext}"/> 来包含由其它
/// 包括模型根利用生产者工厂,仅需要参考元数据。
/// 通过全面实施,外部模型及其生成的编码和复印机被消耗
/// 提供完整的依赖性图表,包括私人国家和专门实体的
/// 其他消费者可使用同一生产商的参考集会。
/// F# 示范参考声明范本提供了在产生 C# 代码时所需的页头事实
/// 复印机通过充分实施元数据提供。
/// 通用属性需要 C# 11 或以后的特性。
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class GeneratedContextTypeAttribute<T> : Attribute;
