using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;

namespace Orleans.Durable.Journaling;

internal static class OrleansBinaryCollectionWireHelpers
{
    /// <summary>
    /// 对单个快照项数的防御性上限，在恢复期间应用。
    /// 否则损坏的日志可能报告高达 <see cref="int.MaxValue"/> 的计数，
    /// 在读取因其他原因失败之前就触发巨大的内存分配。
    /// </summary>
    internal const int MaxSnapshotItemCount = 50_000_000;

    public static int ReadSnapshotCount<TInput>(ref Reader<TInput> reader)
    {
        var value = reader.ReadVarUInt32();
        if (value > MaxSnapshotItemCount)
        {
            ThrowSnapshotCountTooLarge(value);
        }

        return (int)value;
    }

    public static int ReadListIndex<TInput>(ref Reader<TInput> reader)
        => ConvertWireUInt32ToInt32(reader.ReadVarUInt32(), "list index");

    private static int ConvertWireUInt32ToInt32(uint value, string fieldName)
    {
        if (value > int.MaxValue)
        {
            ThrowIntegerOverflow(fieldName, value);
        }

        return (int)value;
    }

    [DoesNotReturn]
    private static void ThrowSnapshotCountTooLarge(uint value) =>
        throw new InvalidOperationException(
            $"Malformed binary journal entry: snapshot count {value} exceeds the maximum supported value {MaxSnapshotItemCount}.");

    [DoesNotReturn]
    private static void ThrowIntegerOverflow(string fieldName, uint value) =>
        throw new InvalidOperationException($"Malformed binary journal entry: {fieldName} {value} exceeds the maximum supported value {int.MaxValue}.");
}
