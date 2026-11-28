using System;
using System.Diagnostics.Metrics;

namespace Orleans.Runtime;

/// <summary>
/// 提供 Orleans 运行时指标使用的 <see cref="Meter"/>。
/// </summary>
/// <param name="meterFactory">用于创建 Orleans Meter 的 Meter 工厂。</param>
public class OrleansInstruments(IMeterFactory meterFactory)
{
    /// <summary>
    /// 获取 Orleans 运行时 Meter。
    /// </summary>
    public Meter Meter { get; } = (meterFactory ?? throw new ArgumentNullException(nameof(meterFactory))).Create("Microsoft.Orleans");
}
