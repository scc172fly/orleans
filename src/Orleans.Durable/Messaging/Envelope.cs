using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Orleans.Durable.Messaging;

/// <summary>包含指令身份、有效载荷、主题、可选应用头和可选投送时间。</summary>
/// <remarks>
/// 需要信件身份和有效载荷;空有效载荷有效。 Raw 协议可以省略
/// 将有效载荷和信头放在上方,然后发布信息并保存
/// 之后没有变化。 Struct 任务分配共享这些对象; 一般 Orleans 深重复制
/// 和去航空化产生独立的有效载荷阵列、信头字典和信头值阵列。
/// </remarks>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.Envelope")]
public readonly struct Envelope
{
    /// <summary>获取接收箱内精确的应用程序定义命令身份。</summary>
    [Id(0)]
    public required MessageId MessageId { get; init; }

    /// <summary>获得所需的应用有效载荷字节,包括空有效载荷。</summary>
    [Id(1)]
    public required byte[] Payload { get; init; }

    /// <summary>获得非空正统的任择应用协议主题。</summary>
    [Id(2)]
    public string? Subject { get; init; }

    /// <summary>获得可选应用程序定义的用字数头标定。</summary>
    /// <remarks>使用一个例常字典用于区分大小写标题的名称。密钥是非空的、完善的 UTF-16 字符串,而数组是非null 。</remarks>
    [Id(3)]
    public Dictionary<string, byte[]>? Headers { get; init; }

    /// <summary>获得接收收件箱处理信件的可选最早时间。</summary>
    /// <remarks>
    /// <para>
    /// 发件箱立即发送消息。 收件人收件箱接受、 复制并维持
    /// 照常,然后保持到这个时间到达 持久工作钟的时钟, 并有一个持久的觉醒
    /// 当信件到期时恢复 grain 。 grain 发送给自己的邮件被它自己所接受
    /// 发送日记写中的框。 <see langword="null"/> 一旦处理此消息
    /// 接受是持久的。
    /// </para>
    /// <para>
    /// 时间是命令的一部分: 重用一个不同值的待处理信件代号是一个相互矛盾的命令。
    /// 信件从接受到完成,从接收到收件箱 <c>MaxCapacity</c> 的数。
    /// </para>
    /// </remarks>
    [Id(4)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? DeliverAt { get; init; }
}
