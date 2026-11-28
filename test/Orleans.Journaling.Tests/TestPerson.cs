namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 用于复杂对象序列化测试的测试类
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
