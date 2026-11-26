using System;

namespace Orleans.Configuration;

/// <summary>
/// Redis 流接收器选项。
/// </summary>
public sealed class RedisStreamReceiverOptions
{
    /// <summary>
    /// Redis 流信息字段名称 。
    /// </summary>
    public string FieldName { get; set; } = DefaultFieldName;

    /// <summary>
    /// 默认 Redis 流信息字段名称。
    /// </summary>
    public const string DefaultFieldName = "payload";

    /// <summary>
    /// 单读操作中从 Redis 获取条目数 。
    /// </summary>
    public int ReadCount { get; set; } = DefaultReadCount;

    /// <summary>
    /// 在单个读操作中从 Redis 获取的默认条目数。
    /// </summary>
    public const int DefaultReadCount = 1000;
}
