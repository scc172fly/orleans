using Orleans.Serialization.Buffers;
using Orleans.Serialization.Session;

namespace Orleans.Durable.Journaling;

internal sealed class OrleansBinaryPreservedJournalEntry : IPreservedJournalEntry
{
    private readonly byte[] _payload;

    public OrleansBinaryPreservedJournalEntry(ArcBuffer payload, SerializerSessionPool sessionPool)
    {
        ArgumentNullException.ThrowIfNull(sessionPool);
        // 退役各州在读缓冲释放后保留保存的条目;复制字节。
        _payload = payload.ToArray();
    }

    public ReadOnlyMemory<byte> Payload => _payload;

    public string FormatKey => OrleansBinaryJournalFormat.JournalFormatKey;
}
