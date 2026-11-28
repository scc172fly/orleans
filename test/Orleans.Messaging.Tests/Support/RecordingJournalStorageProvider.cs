using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Options;
using Orleans.Durable.Journaling;

namespace Orleans.Durable.Messaging.Tests.Support;

/// <summary>在内存中存储日志，并记录每次成功日志写入的字节。</summary>
public sealed class RecordingJournalStorageProvider : IJournalStorageProvider, IJournalStorageCatalog
{
    private readonly ConcurrentDictionary<JournalId, ConcurrentQueue<byte[]>> _writes = new();
    private readonly ConcurrentDictionary<JournalId, byte> _snapshots = new();
    private VolatileJournalStorageProvider? _inner;

    public void Configure(IOptions<JournaledStateManagerOptions> options) => _inner ??= new VolatileJournalStorageProvider(options);

    /// <summary>请求在日志下一次写入时以快照替换日志。</summary>
    public void RequestSnapshot(JournalId journalId) => _snapshots[journalId] = 0;

    public IJournalStorage CreateStorage(JournalId journalId) =>
        new RecordingJournalStorage(this, journalId, _writes.GetOrAdd(journalId, static _ => new()), Inner.CreateStorage(journalId));

    public IAsyncEnumerable<JournalCatalogEntry> ListAsync(
        JournalCatalogListOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Inner.ListAsync(options, cancellationToken);

    /// <summary>按写入顺序获取日志每次成功写入的字节。</summary>
    public byte[][] GetWrites(JournalId journalId) => _writes.TryGetValue(journalId, out var writes) ? [.. writes] : [];

    /// <summary>
    /// 统计 <paramref name="payload"/> 在日志各次写入中的出现次数：对于 Orleans 二进制格式按原始字节统计，
    /// 对于 JSON 格式按其写入字节数组时使用的 Base64 字符串统计。
    /// </summary>
    public int CountPayloadOccurrences(JournalId journalId, byte[] payload)
    {
        var base64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(payload));
        var count = 0;
        foreach (var write in GetWrites(journalId))
        {
            count += CountOccurrences(write, payload) + CountOccurrences(write, base64);
        }

        return count;
    }

    private static int CountOccurrences(ReadOnlySpan<byte> data, ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            return 0;
        }

        var count = 0;
        for (var index = data.IndexOf(value); index >= 0; index = data.IndexOf(value))
        {
            count++;
            data = data[(index + value.Length)..];
        }

        return count;
    }

    private VolatileJournalStorageProvider Inner =>
        _inner ?? throw new InvalidOperationException("The recording journal storage provider has not been configured.");

    private sealed class RecordingJournalStorage(
        RecordingJournalStorageProvider owner,
        JournalId journalId,
        ConcurrentQueue<byte[]> writes,
        IJournalStorage inner) : IJournalStorage
    {
        public bool IsCompactionRequested => owner._snapshots.ContainsKey(journalId) || inner.IsCompactionRequested;

        public ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken) =>
            inner.ReadAsync(consumer, cancellationToken);

        public ValueTask<bool> CreateIfNotExistsAsync(
            IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default) =>
            inner.CreateIfNotExistsAsync(metadata, cancellationToken);

        public ValueTask<IJournalMetadata?> GetMetadataAsync(CancellationToken cancellationToken = default) =>
            inner.GetMetadataAsync(cancellationToken);

        public ValueTask<IJournalMetadata?> UpdateMetadataAsync(
            IReadOnlyDictionary<string, string>? set = null,
            IEnumerable<string>? remove = null,
            string? expectedETag = null,
            CancellationToken cancellationToken = default) =>
            inner.UpdateMetadataAsync(set, remove, expectedETag, cancellationToken);

        public async ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        {
            var bytes = value.ToArray();
            await inner.ReplaceAsync(value, cancellationToken).ConfigureAwait(false);
            owner._snapshots.TryRemove(journalId, out _);
            writes.Enqueue(bytes);
        }

        public async ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        {
            var bytes = value.ToArray();
            await inner.AppendAsync(value, cancellationToken).ConfigureAwait(false);
            writes.Enqueue(bytes);
        }

        public ValueTask DeleteAsync(CancellationToken cancellationToken) => inner.DeleteAsync(cancellationToken);
    }
}
