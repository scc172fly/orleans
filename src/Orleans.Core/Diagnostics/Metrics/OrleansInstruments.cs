using System;
using System.Diagnostics.Metrics;

namespace Orleans.Runtime;

/// <summary>
/// 提供 <see cref="Meter"/> 使用用 Orleans 运行时间指标。
/// </summary>
/// <param name="meterFactory">用来制造 Orleans 公尺。</param>
public class OrleansInstruments(IMeterFactory meterFactory)
{
    /// <summary>
    /// 得到 Orleans 运行计。
    /// </summary>
    public Meter Meter { get; } = (meterFactory ?? throw new ArgumentNullException(nameof(meterFactory))).Create("Microsoft.Orleans");
}
