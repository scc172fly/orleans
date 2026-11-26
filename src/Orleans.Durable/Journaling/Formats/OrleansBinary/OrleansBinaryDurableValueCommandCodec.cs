using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 耐久值日记条目的二进制代码,保存遗留的双进制电线格式 Orleans 。
/// </summary>
internal sealed class OrleansBinaryDurableValueCommandCodec<T>(
    IFieldCodec<T> codec,
    SerializerSessionPool sessionPool) : IDurableValueCommandCodec<T>
{
    private const uint SetValueCommand = 0;

    /// <inheritdoc/>
    public void WriteSet(T value, JournalStreamWriter writer)
    {
        using var entry = writer.BeginEntry();
        var output = entry.Writer;
        OrleansBinaryCommandCodecHelpers.WriteHeader(output, SetValueCommand);
        OrleansBinaryCommandCodecHelpers.WriteValue(codec, value, output, sessionPool);
        entry.Commit();
    }

    /// <inheritdoc/>
    public void Apply(JournalBufferReader input, IDurableValueCommandHandler<T> consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        using var slice = input.Peek(input.Length);
        using var session = sessionPool.GetSession();
        var reader = Reader.Create(slice, session);
        Apply(ref reader, consumer);
        if (reader.Position != reader.Length)
        {
            throw new InvalidOperationException("Unexpected trailing data after binary journal command.");
        }
    }

    private void Apply<TInput>(ref Reader<TInput> reader, IDurableValueCommandHandler<T> consumer)
    {
        var command = reader.ReadVarUInt32();
        switch (command)
        {
            case SetValueCommand:
                consumer.ApplySet(OrleansBinaryCommandCodecHelpers.ReadValue(codec, ref reader));
                break;
            default:
                throw new NotSupportedException($"Command type {command} is not supported");
        }
    }
}
