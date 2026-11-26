using System;

namespace Orleans.Dashboard;

/// <summary>
/// 以 grain 类或方法进行禁止性特征分析,防止 Orleans 戴斯板剖面图仪跟踪这些特征分析。
/// 应用此属性将特定 grains 或方法排除在性能衡量收集之外。
/// </summary>
/// <example>
/// <code>
/// [NoProfiling]
/// public class MyInternalGrain : Grain
/// {
///     // 这个 Grain 不会被分析
/// }
///
/// public class MyGrain : Grain
/// {
///     [NoProfiling]
///     public Task InternalMethod()
///     {
///         // 此方法不会被分析
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class NoProfilingAttribute : Attribute
{
}
