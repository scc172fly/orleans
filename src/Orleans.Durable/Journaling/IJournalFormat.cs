namespace Orleans.Durable.Journaling;

/// <summary>
/// 读取及写入用于持久化状态日志条目的物理字节格式。
/// </summary>
/// <remarks>
/// 日志格式负责条目的物理分帧。持久化状态编解码器仅写入
/// 单个条目的有效载荷。
/// </remarks>
public interface IJournalFormat
{
    /// <summary>
    /// 获取在存储元数据和带键服务注册中标识此日志格式的键。
    /// </summary>
    string FormatKey { get; }

    /// <summary>
    /// 获取使用此格式写入的日志存储的 MIME 类型，或使用 <see langword="null"/> 表示采用存储提供程序的默认类型。
    /// </summary>
    string? MimeType { get; }

    /// <summary>
    /// 为新的可变日志批次创建写入器。
    /// </summary>
    /// <returns>新的日志缓冲区写入器。</returns>
    JournalBufferWriter CreateWriter();

    /// <summary>
    /// 从 <paramref name="input"/> 读取尽可能多的完整日志条目，并将它们应用到已解析的状态。
    /// </summary>
    /// <param name="input">缓冲的已持久化日志数据，包括其完成状态。</param>
    /// <param name="context">用于解析状态及日志服务的重放上下文。</param>
    /// <remarks>
    /// 如果 <see cref="JournalBufferReader.IsCompleted"/> 为 <see langword="false"/>，则在
    /// 数据不足以读取另一个完整日志条目时返回。如果 <see cref="JournalBufferReader.IsCompleted"/>
    /// 为 <see langword="true"/>，而剩余数据不包含完整日志条目，则此方法抛出异常。
    /// </remarks>
    void Replay(JournalBufferReader input, JournalReplayContext context);
}
