using System.Buffers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 格式化自有日记条目,无需解释即可复制前置。
/// </summary>
public interface IPreservedJournalEntry
{
    /// <summary>
    /// 获取保存此条目的日记格式密钥。
    /// </summary>
    string FormatKey { get; }

    /// <summary>
    /// 获取保存条目的输入有效载荷字节。
    /// </summary>
    ReadOnlyMemory<byte> Payload { get; }
}

internal sealed class PreservedJournalEntry : IPreservedJournalEntry
{
    private readonly byte[] _payload;

    public PreservedJournalEntry(string formatKey, ReadOnlySequence<byte> payload)
    {
        FormatKey = JournalFormatServices.ValidateJournalFormatKey(formatKey);
        _payload = payload.ToArray();
    }

    public PreservedJournalEntry(string formatKey, JournalBufferReader payload)
    {
        FormatKey = JournalFormatServices.ValidateJournalFormatKey(formatKey);
        _payload = payload.ToArray();
    }

    public PreservedJournalEntry(string formatKey, ReadOnlyMemory<byte> payload)
    {
        FormatKey = JournalFormatServices.ValidateJournalFormatKey(formatKey);
        _payload = payload.ToArray();
    }

    public string FormatKey { get; }

    public ReadOnlyMemory<byte> Payload => _payload;
}
