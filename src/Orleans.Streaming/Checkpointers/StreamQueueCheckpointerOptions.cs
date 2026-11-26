using System;
using System.Collections.Generic;

namespace Orleans.Streams;

/// <summary>
/// 配置 <see cref="StreamQueueCheckpointer"/>。
/// </summary>
public sealed class StreamQueueCheckpointerOptions
{
    /// <summary>
    /// 获取或设定检查站之间的最小间隔。
    /// </summary>
    public TimeSpan PersistInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 获取或设置用来防止检查站向后移动的比较器。
    /// </summary>
    /// <remarks>
    /// 当该财产 <see langword="null"/>时,假定检查站会越来越频繁地抵达。
    /// </remarks>
    public IComparer<string>? CheckpointComparer { get; set; }
}
