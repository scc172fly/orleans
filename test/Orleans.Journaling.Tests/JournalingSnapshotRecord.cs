namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 用于进行 Orleans 实地编码的编码器快照测试所用的复合体试验类型c
/// 多字段记录(字符串+内特+嵌套结构)的行为行为。
/// <see cref="GenerateSerializerAttribute"/> + <see cref="IdAttribute"/> ID 插上电线
/// 形状,可以复制 <c>snapshots/</c> 中的快照文件。
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
/// <see cref="JournalingSnapshotRecord"/>的内嵌支架成员。 存在如此二进制
/// 快速操作 定位的野地码线布局 而不仅仅是平面的卡路里有效载荷
/// </summary>
[GenerateSerializer]
public readonly record struct JournalingSnapshotRecordTag
{
    [Id(0)]
    public string Label { get; init; }

    [Id(1)]
    public int Code { get; init; }
}
