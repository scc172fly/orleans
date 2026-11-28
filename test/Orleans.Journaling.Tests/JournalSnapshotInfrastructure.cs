using System.Buffers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans.Serialization.Buffers;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 与编解码器无关的快照格式化辅助工具，供二进制和 JSONL 编解码器快照测试共同使用。
/// </summary>
/// <remarks>
/// 这些辅助工具特意不依赖任何特定的命令鉴别值。OrleansBinary 的
/// 字典、列表、队列、集合、值和状态编解码器使用 varuint 命令 ID，而 TaskCompletionSource
/// 编解码器使用单个状态字节。仅解析通用帧结构（条目长度前缀和流 ID），
/// 可使辅助工具适用于所有编解码器系列，同时仍呈现每个条目的边界，
/// 方便审查人员阅读快照差异。
/// </remarks>
public static class JournalSnapshotFormatting
{
    /// <summary>
    /// 返回 <paramref name="bytes"/> 的大写、无分隔符十六进制表示。
    /// </summary>
    /// <remarks>
    /// 这是用作二进制快照文件首行的字节级基准：传输格式的任何
    /// 偏差都会改变这一行并导致 CI 失败。
    /// </remarks>
    public static string HexBaseline(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    /// <summary>
    /// 将 OrleansBinary 日志段呈现为便于查看快照的多行文本。
    /// </summary>
    /// <remarks>
    /// 输出从单行 <c>HEX: …</c>（字节级基准）开始，随后是
    /// <c>DISASSEMBLY:</c> 部分。反汇编结果为每个条目输出一行标题，包含
    /// OrleansBinary 读取器解析的通用帧字段（条目帧版本、条目主体长度
    /// 和流 ID），以及命令有效载荷的十六进制和 ASCII 转储（每行 16 字节）。如果
    /// 日志段格式错误，反汇编会退回到直接转储尚未解析的尾部；这样即使面对
    /// 恶意构造的输入，辅助工具也能安全使用，不会在生成快照时抛出异常。
    /// </remarks>
    public static string FormatBinary(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder();
        builder.Append("HEX: ").Append(HexBaseline(bytes)).Append('\n');
        builder.Append("DISASSEMBLY:\n");

        if (bytes.Length == 0)
        {
            builder.Append("(empty)\n");
            return builder.ToString();
        }

        // 复制到数组，以便在循环内部复用 byte[]，而不受跨方法边界的
        // span 语义限制（StringBuilder 调用及十六进制/ASCII 转储辅助方法）。
        var data = bytes.ToArray();
        using var bufferWriter = new ArcBufferWriter();
        bufferWriter.Write(data);
        using var buffer = bufferWriter.PeekSlice(bufferWriter.Length);
        var entryIndex = 0;
        var offset = 0;

        while (offset < data.Length)
        {
            byte framingVersion;
            uint bodyLength;
            int prefixSize;
            var hasFrameHeader = false;
            try
            {
                hasFrameHeader = OrleansBinaryJournalReader.TryReadVersionAndLength(
                    buffer.UnsafeSlice(offset, data.Length - offset),
                    out framingVersion,
                    out bodyLength,
                    out prefixSize);
            }
            catch
            {
                framingVersion = OrleansBinaryJournalReader.LegacyFramingVersion;
                bodyLength = 0;
                prefixSize = 0;
            }

            if (!hasFrameHeader)
            {
                builder.Append("(unable to parse entry version/length prefix at offset ")
                    .Append(offset)
                    .Append(")\n");
                AppendHexAsciiDump(builder, data.AsSpan(offset));
                return builder.ToString();
            }

            var entryStart = offset + prefixSize;
            var entryEnd = entryStart + (int)bodyLength;
            if (bodyLength == 0 || entryEnd > data.Length)
            {
                builder.Append("(malformed entry at offset ")
                    .Append(offset)
                    .Append(": body length=")
                    .Append(bodyLength)
                    .Append(", remaining=")
                    .Append(data.Length - entryStart)
                    .Append(")\n");
                AppendHexAsciiDump(builder, data.AsSpan(offset));
                return builder.ToString();
            }

            ulong streamId;
            int streamIdSize;
            if (framingVersion == OrleansBinaryJournalReader.FramingVersion)
            {
                if (bodyLength < sizeof(uint))
                {
                    builder.Append("[entry ").Append(entryIndex)
                        .Append("] length=").Append(bodyLength)
                        .Append(" (missing fixed-width stream id)\n");
                    AppendHexAsciiDump(builder, data.AsSpan(entryStart, (int)bodyLength));
                    offset = entryEnd;
                    entryIndex++;
                    continue;
                }

                streamId = OrleansBinaryJournalReader.ReadUInt32LittleEndian(buffer.UnsafeSlice(entryStart, sizeof(uint)));
                streamIdSize = sizeof(uint);
            }
            else if (!TryDecodeVarUInt(buffer.UnsafeSlice(entryStart, (int)bodyLength), out streamId, out streamIdSize))
            {
                builder.Append("[entry ").Append(entryIndex).Append("] length=").Append(bodyLength)
                    .Append(" (unable to parse varuint32 stream id)\n");
                AppendHexAsciiDump(builder, data.AsSpan(entryStart, (int)bodyLength));
                offset = entryEnd;
                entryIndex++;
                continue;
            }

            var operationStart = entryStart + streamIdSize;
            var operationLength = entryEnd - operationStart;
            if (operationLength <= 0)
            {
                builder.Append("[entry ").Append(entryIndex)
                    .Append("] frame-version=").Append(framingVersion)
                    .Append(" length=").Append(bodyLength)
                    .Append(" streamId=").Append(streamId)
                    .Append(" (missing command payload)\n");
                offset = entryEnd;
                entryIndex++;
                continue;
            }

            builder.Append("[entry ").Append(entryIndex)
                .Append("] frame-version=").Append(framingVersion)
                .Append(" length=").Append(bodyLength)
                .Append(" streamId=").Append(streamId)
                .Append(" payload-bytes=");

            int payloadStart;
            int payloadLength;
            if (framingVersion == OrleansBinaryJournalReader.LegacyFramingVersion)
            {
                var legacyCommandVersion = data[operationStart];
                payloadStart = operationStart + 1;
                payloadLength = operationLength - 1;
                builder.Append(payloadLength)
                    .Append(" legacy-command-version=").Append(legacyCommandVersion)
                    .Append('\n');
            }
            else
            {
                payloadStart = operationStart;
                payloadLength = operationLength;
                builder.Append(payloadLength).Append('\n');
            }

            AppendHexAsciiDump(builder, data.AsSpan(payloadStart, payloadLength));

            offset = entryEnd;
            entryIndex++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// 解码一个 Orleans 格式的 varint（变长无符号整数）。在以下情况下返回 <c>false</c>：
    /// 输入不包含完整的 varint。
    /// </summary>
    private static bool TryDecodeVarUInt(ArcBuffer input, out ulong value, out int bytesRead)
    {
        value = 0;
        bytesRead = 0;
        if (input.Length == 0)
        {
            return false;
        }

        try
        {
            var reader = Reader.Create(input, session: null!);
            value = reader.ReadVarUInt64();
            bytesRead = (int)reader.Position;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 将规范化的 <c>OFFSET  16-byte hex  | ASCII</c> 格式的 <paramref name="bytes"/> 转储追加到
    /// <paramref name="builder"/>。输出使用字面量 <c>\n</c>，因此生成的文本在
    /// Windows 和 Linux CI 中保持一致，无需依赖 <see cref="Environment.NewLine"/>。
    /// </summary>
    private static void AppendHexAsciiDump(StringBuilder builder, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            builder.Append("    (no payload)\n");
            return;
        }

        const int Width = 16;
        for (var i = 0; i < bytes.Length; i += Width)
        {
            var rowLength = Math.Min(Width, bytes.Length - i);
            builder.Append(i.ToString("X4"));
            builder.Append("  ");

            for (var j = 0; j < Width; j++)
            {
                if (j == Width / 2)
                {
                    builder.Append(' ');
                }

                if (j < rowLength)
                {
                    builder.Append(bytes[i + j].ToString("X2"));
                    builder.Append(' ');
                }
                else
                {
                    builder.Append("   ");
                }
            }

            builder.Append("  |");
            for (var j = 0; j < rowLength; j++)
            {
                var b = bytes[i + j];
                builder.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }

            builder.Append("|\n");
        }
    }
}

public static class JournalTestReplayContext
{
    public static JournalReplayContext Create(string journalFormatKey, params (JournalStreamId StreamId, IStateMachine State)[] states)
    {
        var manager = CreateManager(journalFormatKey);
        foreach (var (streamId, state) in states)
        {
            manager.BindStateForReplay(streamId, state);
        }

        return new(manager);
    }

    private static JournaledStateManager CreateManager(string journalFormatKey)
    {
        journalFormatKey = JournalFormatServices.ValidateJournalFormatKey(journalFormatKey);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IJournalFormat>(journalFormatKey, new TestJournalFormat(journalFormatKey));
        services.AddKeyedSingleton<IDurableDictionaryCommandCodec<string, uint>>(journalFormatKey, new UnsupportedDictionaryCommandCodec<uint>());
        services.AddKeyedSingleton<IDurableDictionaryCommandCodec<string, DateTime>>(journalFormatKey, new UnsupportedDictionaryCommandCodec<DateTime>());

        var serviceProvider = services.BuildServiceProvider();
        var shared = new JournaledStateManagerShared(
            NullLogger<JournaledStateManager>.Instance,
            Options.Create(new JournaledStateManagerOptions { JournalFormatKey = journalFormatKey }),
            TimeProvider.System,
            serviceProvider);

        return new JournaledStateManager(shared, new NullJournalStorage());
    }

    private sealed class TestJournalFormat(string journalFormatKey) : IJournalFormat
    {
        public string FormatKey { get; } = journalFormatKey;

        public string? MimeType => null;

        public JournalBufferWriter CreateWriter() => new OrleansBinaryJournalBufferWriter();

        public void Replay(JournalBufferReader input, JournalReplayContext context) => throw new NotSupportedException();
    }

    private sealed class UnsupportedDictionaryCommandCodec<TValue> : IDurableDictionaryCommandCodec<string, TValue>
    {
        public void WriteSet(string key, TValue value, JournalStreamWriter writer) => throw new NotSupportedException();

        public void WriteRemove(string key, JournalStreamWriter writer) => throw new NotSupportedException();

        public void WriteClear(JournalStreamWriter writer) => throw new NotSupportedException();

        public void WriteSnapshot(IReadOnlyCollection<KeyValuePair<string, TValue>> items, JournalStreamWriter writer) => throw new NotSupportedException();

        public void Apply(JournalBufferReader input, IDurableDictionaryCommandHandler<string, TValue> consumer) => throw new NotSupportedException();
    }

    private sealed class NullJournalStorage : IJournalStorage
    {
        public bool IsCompactionRequested => false;

        public ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken) => default;

        public ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) => default;

        public ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) => default;

        public ValueTask DeleteAsync(CancellationToken cancellationToken) => default;
    }
}

/// <summary>
/// 用于字典编解码器快照测试的记录型 <see cref="IStateMachine"/>。它同时实现
/// 状态契约（使日志读取器能够向它分派操作）和字典操作处理器
/// （使编解码器通过 <c>state is THandler</c> 接受它）。
/// </summary>
public sealed class RecordingDictionaryState<TKey, TValue> : IStateMachine, IDurableDictionaryCommandHandler<TKey, TValue>
    where TKey : notnull
{
    private readonly IDurableDictionaryCommandCodec<TKey, TValue> _codec;
    private readonly RecordingDictionaryCommandHandler<TKey, TValue> _handler = new();

    public RecordingDictionaryState(IDurableDictionaryCommandCodec<TKey, TValue> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _codec = codec;
    }

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, _codec).Apply(entry.Reader, this);

    public IReadOnlyList<string> Commands => _handler.Commands;

    public IReadOnlyList<KeyValuePair<TKey, TValue>> SnapshotItems => _handler.SnapshotItems;

    public void ApplySet(TKey key, TValue value) => _handler.ApplySet(key, value);

    public void ApplyRemove(TKey key) => _handler.ApplyRemove(key);

    public void ApplyClear() => _handler.ApplyClear();

    public void Reset(int capacityHint) => _handler.Reset(capacityHint);

    public void Reset(JournalStreamWriter writer) { }

    public void WritePendingEntries(JournalStreamWriter writer) { }

    public void WriteSnapshot(JournalStreamWriter writer) { }

}

/// <summary>用于列表编解码器快照测试的记录型状态。</summary>
public sealed class RecordingListState<T> : IStateMachine, IDurableListCommandHandler<T>
{
    private readonly IDurableListCommandCodec<T> _codec;
    private readonly RecordingListCommandHandler<T> _handler = new();

    public RecordingListState(IDurableListCommandCodec<T> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _codec = codec;
    }

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, _codec).Apply(entry.Reader, this);

    public IReadOnlyList<string> Commands => _handler.Commands;

    public void ApplyAdd(T item) => _handler.ApplyAdd(item);

    public void ApplySet(int index, T item) => _handler.ApplySet(index, item);

    public void ApplyInsert(int index, T item) => _handler.ApplyInsert(index, item);

    public void ApplyRemoveAt(int index) => _handler.ApplyRemoveAt(index);

    public void ApplyClear() => _handler.ApplyClear();

    public void Reset(int capacityHint) => _handler.Reset(capacityHint);

    public void Reset(JournalStreamWriter writer) { }

    public void WritePendingEntries(JournalStreamWriter writer) { }

    public void WriteSnapshot(JournalStreamWriter writer) { }

}

/// <summary>用于队列编解码器快照测试的记录型状态。</summary>
public sealed class RecordingQueueState<T> : IStateMachine, IDurableQueueCommandHandler<T>
{
    private readonly IDurableQueueCommandCodec<T> _codec;
    private readonly RecordingQueueCommandHandler<T> _handler = new();

    public RecordingQueueState(IDurableQueueCommandCodec<T> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _codec = codec;
    }

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, _codec).Apply(entry.Reader, this);

    public IReadOnlyList<string> Commands => _handler.Commands;

    public void ApplyEnqueue(T item) => _handler.ApplyEnqueue(item);

    public void ApplyDequeue() => _handler.ApplyDequeue();

    public void ApplyClear() => _handler.ApplyClear();

    public void Reset(int capacityHint) => _handler.Reset(capacityHint);

    public void Reset(JournalStreamWriter writer) { }

    public void WritePendingEntries(JournalStreamWriter writer) { }

    public void WriteSnapshot(JournalStreamWriter writer) { }

}

/// <summary>用于集合编解码器快照测试的记录型状态。</summary>
public sealed class RecordingSetState<T> : IStateMachine, IDurableSetCommandHandler<T>
{
    private readonly IDurableSetCommandCodec<T> _codec;
    private readonly RecordingSetCommandHandler<T> _handler = new();

    public RecordingSetState(IDurableSetCommandCodec<T> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _codec = codec;
    }

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, _codec).Apply(entry.Reader, this);

    public IReadOnlyList<string> Commands => _handler.Commands;

    public void ApplyAdd(T item) => _handler.ApplyAdd(item);

    public void ApplyRemove(T item) => _handler.ApplyRemove(item);

    public void ApplyClear() => _handler.ApplyClear();

    public void Reset(int capacityHint) => _handler.Reset(capacityHint);

    public void Reset(JournalStreamWriter writer) { }

    public void WritePendingEntries(JournalStreamWriter writer) { }

    public void WriteSnapshot(JournalStreamWriter writer) { }

}

/// <summary>用于值编解码器快照测试的记录型状态。</summary>
public sealed class RecordingValueState<T> : IStateMachine, IDurableValueCommandHandler<T>
{
    private readonly IDurableValueCommandCodec<T> _codec;
    private readonly RecordingValueCommandHandler<T> _handler = new();

    public RecordingValueState(IDurableValueCommandCodec<T> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _codec = codec;
    }

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, _codec).Apply(entry.Reader, this);

    public T? Value => _handler.Value;

    public bool ValueWasSet { get; private set; }

    public void ApplySet(T value)
    {
        _handler.ApplySet(value);
        ValueWasSet = true;
    }

    public void Reset(JournalStreamWriter writer) { }

    public void WritePendingEntries(JournalStreamWriter writer) { }

    public void WriteSnapshot(JournalStreamWriter writer) { }

}

/// <summary>用于状态编解码器快照测试的记录型状态。</summary>
public sealed class RecordingStateState<T> : IStateMachine, IPersistentStateCommandHandler<T>
{
    private readonly IPersistentStateCommandCodec<T> _codec;
    private readonly RecordingPersistentStateCommandHandler<T> _handler = new();

    public RecordingStateState(IPersistentStateCommandCodec<T> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _codec = codec;
    }

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, _codec).Apply(entry.Reader, this);

    public IReadOnlyList<string> Commands => _handler.Commands;

    public T? State => _handler.State;

    public ulong Version => _handler.Version;

    public void ApplySet(T state, ulong version) => _handler.ApplySet(state, version);

    public void ApplyClear() => _handler.ApplyClear();

    public void Reset(JournalStreamWriter writer) { }

    public void WritePendingEntries(JournalStreamWriter writer) { }

    public void WriteSnapshot(JournalStreamWriter writer) { }

}

/// <summary>用于任务完成源编解码器快照测试的记录型状态。</summary>
public sealed class RecordingTcsState<T> : IStateMachine, IDurableTaskCompletionSourceCommandHandler<T>
{
    private readonly IDurableTaskCompletionSourceCommandCodec<T> _codec;
    private readonly RecordingTaskCompletionSourceCommandHandler<T> _handler = new();

    public RecordingTcsState(IDurableTaskCompletionSourceCommandCodec<T> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _codec = codec;
    }

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, _codec).Apply(entry.Reader, this);

    public IReadOnlyList<string> Commands => _handler.Commands;

    public void ApplyPending() => _handler.ApplyPending();

    public void ApplyCompleted(T value) => _handler.ApplyCompleted(value);

    public void ApplyFaulted(Exception exception) => _handler.ApplyFaulted(exception);

    public void ApplyCanceled() => _handler.ApplyCanceled();

    public void Reset(JournalStreamWriter writer) { }

    public void WritePendingEntries(JournalStreamWriter writer) { }

    public void WriteSnapshot(JournalStreamWriter writer) { }

}

public sealed class RecordingLogState<T> : IStateMachine, IDurableLogCommandHandler<T>
{
    private readonly IDurableLogCommandCodec<T> _codec;
    private readonly List<string> _commands = [];

    public RecordingLogState(IDurableLogCommandCodec<T> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _codec = codec;
    }

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, _codec).Apply(entry.Reader, this);

    public IReadOnlyList<string> Commands => _commands;

    public void ApplyAppend(T item) => _commands.Add($"append:{item}");

    public void ApplyDraftCommit(LogDraftCommit commit) => _commands.Add($"commitDraft:{commit.DraftId}:{commit.Sequence}:{commit.Count}");

    public void ApplyTrimBefore(long sequence) => _commands.Add($"trimBefore:{sequence}");

    public void ApplyClear() => _commands.Add("clear");

    public void ApplySeal() => _commands.Add("seal");

    public void Reset(long epoch, long firstSequence, long resetSequence, int capacityHint) =>
        _commands.Add($"snapshot:{epoch}:{firstSequence}:{resetSequence}:{capacityHint}");

    public void Reset(JournalStreamWriter writer) { }

    public void WritePendingEntries(JournalStreamWriter writer) { }

    public void WriteSnapshot(JournalStreamWriter writer) { }
}
