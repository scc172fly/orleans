using System;
using System.Collections.Generic;

namespace Orleans.Streams;

/// <summary>
/// 配置 <see cref="StreamQueueCheckpointer"/>。
/// </summary>
public sealed class StreamQueueCheckpointerOptions
{
    /// <summary>
    /// 获取或设置检查点写入之间的最短时间间隔。
    /// </summary>
    public TimeSpan PersistInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 获取或设置用于防止检查点向后移动的比较器。
    /// </summary>
    /// <remarks>
    /// 当此属性为 <see langword="null"/> 时，假定检查点按递增顺序到达。
    /// </remarks>
    public IComparer<string>? CheckpointComparer { get; set; }
}
