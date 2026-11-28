using Orleans.Serialization.Buffers;
using Orleans.Serialization.Session;

namespace Orleans.Durable.Journaling;

internal sealed class OrleansBinaryPreservedJournalEntry : IPreservedJournalEntry
{
    private readonly byte[] _payload;

    public OrleansBinaryPreservedJournalEntry(ArcBuffer payload, SerializerSessionPool sessionPool)
    {
        ArgumentNullException.ThrowIfNull(sessionPool);
        // 读取缓冲区释放后，已退役状态仍保留其保留条目；因此复制字节。
        _payload = payload.ToArray();
    }

    public ReadOnlyMemory<byte> Payload => _payload;

    public string FormatKey => OrleansBinaryJournalFormat.JournalFormatKey;
}
