using System;
using System.Runtime.CompilerServices;

namespace Orleans.Dashboard.Metrics;

/// <summary>
/// 提供 grain 方法职业特征分析能力,跟踪执行时间和故障。
/// </summary>
internal interface IGrainProfiler
{
    /// <summary>
    /// a 记录为貌相目的援引 grain 方法的情况。
    /// </summary>
    /// <param name="elapsedMs">方法调用的过长时间(以毫秒计) 。</param>
    /// <param name="grainType">类型 grain.</param>
    /// <param name="methodName">所引用的方法的名称。 如果未指定, 从调用者中自动捕获 。</param>
    /// <param name="failed">如果援引方法导致例外, 则正确; 否则, 虚假 。</param>
    void Track(double elapsedMs, Type grainType, [CallerMemberName] string? methodName = null, bool failed = false);

    /// <summary>
    /// 启用或禁用 grain 配置文件程序。
    /// </summary>
    /// <param name="enabled">正确, 以便进行剖析; 假以禁用 。</param>
    void Enable(bool enabled);

    /// <summary>
    /// 获得一个值,以显示 grain 配置文件是否当前启用。
    /// </summary>
    bool IsEnabled { get; }
}
