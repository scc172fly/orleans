using System;
using System.Runtime.CompilerServices;

namespace Orleans.Dashboard.Metrics;

/// <summary>
/// 提供 Grain 方法调用的性能分析功能，跟踪执行时间和失败情况。
/// </summary>
internal interface IGrainProfiler
{
    /// <summary>
    /// 记录一次 Grain 方法调用，用于性能分析。
    /// </summary>
    /// <param name="elapsedMs">方法调用耗时，单位为毫秒。</param>
    /// <param name="grainType">Grain 的类型。</param>
    /// <param name="methodName">被调用的方法名称。未指定时自动从调用方获取。</param>
    /// <param name="failed">方法调用导致异常时为 true；否则为 false。</param>
    void Track(double elapsedMs, Type grainType, [CallerMemberName] string? methodName = null, bool failed = false);

    /// <summary>
    /// 启用或禁用 Grain 性能分析器。
    /// </summary>
    /// <param name="enabled">true 表示启用性能分析；false 表示禁用。</param>
    void Enable(bool enabled);

    /// <summary>
    /// 获取一个值，指示当前是否已启用 Grain 性能分析器。
    /// </summary>
    bool IsEnabled { get; }
}
