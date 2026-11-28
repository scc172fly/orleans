using System;

namespace Orleans.Configuration;

/// <summary>
/// Redis 流接收器选项。
/// </summary>
public sealed class RedisStreamReceiverOptions
{
    /// <summary>
    /// Redis 流消息字段名称。
    /// </summary>
    public string FieldName { get; set; } = DefaultFieldName;

    /// <summary>
    /// 默认的 Redis 流消息字段名称。
    /// </summary>
    public const string DefaultFieldName = "payload";

    /// <summary>
    /// 单次读取操作从 Redis 获取的条目数量。
    /// </summary>
    public int ReadCount { get; set; } = DefaultReadCount;

    /// <summary>
    /// 单次读取操作从 Redis 获取的默认条目数量。
    /// </summary>
    public const int DefaultReadCount = 1000;
}
