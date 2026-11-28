using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orleans.Durable.Journaling.Json.Tests;

[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(ThrowingJsonValue))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(JsonCodecTestValue))]
[JsonSerializable(typeof(JournalingSnapshotRecord))]
internal partial class JsonCodecTestJsonContext : JsonSerializerContext;

internal sealed record JsonCodecTestValue(string Name, int Count);

/// <summary>
/// <see cref="Orleans.Durable.Journaling.Tests.JournalingSnapshotRecord"/> 在 JSON 侧的对应实现：
/// 快照测试在两侧使用逻辑上相同的记录结构，使二进制与 JSONL
/// 覆盖矩阵保持一致。
/// </summary>
internal sealed record JournalingSnapshotRecord(string Name, int Count, JournalingSnapshotRecordTag Tag);

/// <summary>
/// <see cref="Orleans.Durable.Journaling.Tests.JournalingSnapshotRecordTag"/> 在 JSON 侧的对应实现。
/// </summary>
internal readonly record struct JournalingSnapshotRecordTag(string Label, int Code);

[JsonConverter(typeof(ThrowingJsonValueConverter))]
internal sealed record ThrowingJsonValue(string Name);

internal sealed class ThrowingJsonValueConverter : JsonConverter<ThrowingJsonValue>
{
    public override ThrowingJsonValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

    public override void Write(Utf8JsonWriter writer, ThrowingJsonValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(nameof(ThrowingJsonValue.Name), value.Name);
        throw new InvalidOperationException("The test converter failed after writing partial JSON.");
    }
}
