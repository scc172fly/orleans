namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 用于复杂物体序列化测试的试验类
/// </summary>
[GenerateSerializer]
public record class TestPerson
{
    [Id(0)]
    public int Id { get; set; }
    [Id(1)]
    public string? Name { get; set; }
    [Id(2)]
    public int Age { get; set; }
}
