using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Orleans.Durable.Messaging;

/// <summary>包含命令标识、有效载荷、主题、可选应用标头以及可选投递时间。</summary>
/// <remarks>
/// 消息标识和有效载荷是必需的；允许空有效载荷。原始协议可省略
/// 主题和标头。在发布消息之前准备好有效载荷与标头，发布之后保持它们
/// 不变。结构体赋值会共享这些对象；普通 Orleans 深拷贝
/// 和反序列化则生成彼此独立的有效载荷数组、标头字典和标头值数组。
/// </remarks>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.Envelope")]
public readonly struct Envelope
{
    /// <summary>获取接收方收件箱中由应用定义的精确命令标识。</summary>
    [Id(0)]
    public required MessageId MessageId { get; init; }

    /// <summary>获取必需的应用有效载荷字节，允许为空。</summary>
    [Id(1)]
    public required byte[] Payload { get; init; }

    /// <summary>获取可选的非空应用协议主题，按序数规则比较。</summary>
    [Id(2)]
    public string? Subject { get; init; }

    /// <summary>获取应用定义的可选命名字节数组标头。</summary>
    /// <remarks>使用按序数比较的字典来实现区分大小写的标头名称。键为非空且格式正确的 UTF-16 字符串，数组不可为 null。</remarks>
    [Id(3)]
    public Dictionary<string, byte[]>? Headers { get; init; }

    /// <summary>获取可选的最早处理时间，接收方收件箱不得早于该时间处理消息。</summary>
    /// <remarks>
    /// <para>
    /// 发件箱立即投递消息。接收方收件箱照常接纳、去重并持久化消息，
    /// 随后保留该消息，直至 Durable Jobs 时钟到达指定时间；一次持久化唤醒
    /// 会在消息到期时恢复 Grain。Grain 发送给自身的消息由其自身
    /// 收件箱在发送日志写入中接纳。<see langword="null"/> 在消息的
    /// 接纳已持久化后立即处理该消息。
    /// </para>
    /// <para>
    /// 时间是命令的一部分：以不同的时间值复用待处理消息的 ID 构成冲突命令。
    /// 消息从接纳到处理完成期间始终计入收件箱的 <c>MaxCapacity</c>。
    /// </para>
    /// </remarks>
    [Id(4)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? DeliverAt { get; init; }
}
