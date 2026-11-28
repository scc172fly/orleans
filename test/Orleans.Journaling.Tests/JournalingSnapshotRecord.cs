namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 编解码器快照测试使用的复合测试类型，用于检验 Orleans 字段编解码器
/// 针对多字段记录（string + int + 嵌套结构体）的行为。稳定的
/// <see cref="GenerateSerializerAttribute"/> + <see cref="IdAttribute"/> 标识固定了传输
/// 格式，使 <c>snapshots/</c> 中的快照文件可以复现。
/// </summary>
[GenerateSerializer]
public sealed record class JournalingSnapshotRecord
{
    [Id(0)]
    public string Name { get; init; } = string.Empty;

    [Id(1)]
    public int Count { get; init; }

    [Id(2)]
    public JournalingSnapshotRecordTag Tag { get; init; }
}

/// <summary>
/// <see cref="JournalingSnapshotRecord"/> 的嵌套结构体成员。用于使二进制
/// 快照检验嵌套字段编解码器的传输布局，而不仅仅是扁平的标量有效载荷。
/// </summary>
[GenerateSerializer]
public readonly record struct JournalingSnapshotRecordTag
{
    [Id(0)]
    public string Label { get; init; }

    [Id(1)]
    public int Code { get; init; }
}
