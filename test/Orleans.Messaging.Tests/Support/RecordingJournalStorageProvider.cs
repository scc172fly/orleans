using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Options;
using Orleans.Durable.Journaling;

namespace Orleans.Durable.Messaging.Tests.Support;

/// <summary>储存在记忆中的期刊,记录每个成功日记的字节。</summary>
public sealed class RecordingJournalStorageProvider : IJournalStorageProvider, IJournalStorageCatalog
{
    private readonly ConcurrentDictionary<JournalId, ConcurrentQueue<byte[]>> _writes = new();
    private readonly ConcurrentDictionary<JournalId, byte> _snapshots = new();
    private VolatileJournalStorageProvider? _inner;

    public void Configure(IOptions<JournaledStateManagerOptions> options) => _inner ??= new VolatileJournalStorageProvider(options);

    /// <summary>请求将日记的下一封信以快照替换。</summary>
    public void RequestSnapshot(JournalId journalId) => _snapshots[journalId] = 0;

    public IJournalStorage CreateStorage(JournalId journalId) =>
        new RecordingJournalStorage(this, journalId, _writes.GetOrAdd(journalId, static _ => new()), Inner.CreateStorage(journalId));

    public IAsyncEnumerable<JournalCatalogEntry> ListAsync(
        JournalCatalogListOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Inner.ListAsync(options, cancellationToken);

    /// <summary>将每个成功写入的字节按写序发送到日记。</summary>
    public byte[][] GetWrites(JournalId journalId) => _writes.TryGetValue(journalId, out var writes) ? [.. writes] : [];

    /// <summary>
    /// 计算日记写入的 <paramref name="payload"/> 发生时间,作为 Orleans 二进制中的原始字节
    /// 或作为 JSON 格式为字节数组书写的 base64 字符串。
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
