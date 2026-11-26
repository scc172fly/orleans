namespace Orleans.Durable.Journaling;

/// <summary>
/// 读取并写下用于持续国家日记条目的物理字节格式。
/// </summary>
/// <remarks>
/// 日记格式拥有条目的物理框架。 耐用的国家编码器仅写入
/// 单项输入的有效载荷。
/// </remarks>
public interface IJournalFormat
{
    /// <summary>
    /// 获取在存储元数据和密钥服务注册中识别该日记格式的密钥。
    /// </summary>
    string FormatKey { get; }

    /// <summary>
    /// 获取以此格式写入的日记存储的 MIME 类型,或者 <see langword="null"/> 使用存储提供者的默认值。
    /// </summary>
    string? MimeType { get; }

    /// <summary>
    /// 为新的可变日记批量创建作家 。
    /// </summary>
    /// <returns>新日记缓冲写器。</returns>
    JournalBufferWriter CreateWriter();

    /// <summary>
    /// 尽可能多地从 <paramref name="input"/> 读取完整日记条目,并将其应用到已解决的状态。
    /// </summary>
    /// <param name="input">缓冲的日记数据,包括其完成状态。</param>
    /// <param name="context">用于解决状态和日记服务问题的重播上下文。</param>
    /// <remarks>
    /// <see cref="JournalBufferReader.IsCompleted"/> 是 <see langword="false"/>的,此方法返回
    /// 没有足够的数据读取另一个完整的日记项。 如果 <see cref="JournalBufferReader.IsCompleted"/>
    /// is <see langword="true"/>,如果剩余数据不包含完整的日记条目,则采用此方法。
    /// </remarks>
    void Replay(JournalBufferReader input, JournalReplayContext context);
}
