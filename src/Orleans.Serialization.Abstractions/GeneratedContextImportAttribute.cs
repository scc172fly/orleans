using System;

namespace Orleans;

/// <summary>
/// 导入由另一个生成的上下文提供的封闭型图。
/// </summary>
/// <typeparam name="TContext">具体产生的背景需要包括。</typeparam>
/// <remarks>
/// 将此属性应用到从 <c>Orleans.Serialization.GeneratedContext</c>衍生出来的部分类别。
/// 包括环境在内的环境需要无障碍的无参数构造,并宣布其根基使用
/// 或使用此属性的链接<see cref="GeneratedContextTypeAttribute{T}"/> 。
/// 世代将这些根作为终点处理,并沿着其余的本地图进行翻转。
/// 包括上下文在内的环境提供自己的工厂和依赖性元数据,以便消费者可以使用
/// 包括以单环图形式显示,确保每个根都有自定义的上下文。
/// 共享链接被配置为每个类型最显眼的提供者密钥一次 。
/// 使用 <c>AddGeneratedContext</c>登记由此得出的上下文。
/// 通用属性需要 C# 11 或以后的特性。
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class GeneratedContextImportAttribute<TContext> : Attribute;
