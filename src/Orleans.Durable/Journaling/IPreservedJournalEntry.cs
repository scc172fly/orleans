using System.Buffers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 由格式拥有的日志条目，可以在不解释其内容的情况下向后续日志复制。
/// </summary>
public interface IPreservedJournalEntry
{
    /// <summary>
    /// 获取此保留条目的日志格式键。
    /// </summary>
    string FormatKey { get; }

    /// <summary>
    /// 获取保留条目的有效载荷字节。
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
