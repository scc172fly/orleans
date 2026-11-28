using System;

namespace Orleans.Dashboard;

/// <summary>
/// 禁止对指定 Grain 类或方法进行性能分析，使 Orleans Dashboard 分析器不再跟踪它们。
/// 应用此特性可将指定 Grain 或方法排除在性能指标采集范围之外。
/// </summary>
/// <example>
/// <code>
/// [NoProfiling]
/// public class MyInternalGrain : Grain
/// {
///     // 不对整个 Grain 进行性能分析
/// }
///
/// public class MyGrain : Grain
/// {
///     [NoProfiling]
///     public Task InternalMethod()
///     {
///         // 不对这个特定方法进行性能分析
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class NoProfilingAttribute : Attribute
{
}
