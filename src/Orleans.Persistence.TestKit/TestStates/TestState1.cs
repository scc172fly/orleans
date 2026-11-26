namespace Orleans.Persistence.TestKit;

/// <summary>
/// 用于验证存储提供者功能的测试状态。
/// </summary>
[Serializable]
[GenerateSerializer]
public class TestState1 : IEquatable<TestState1>
{
    /// <summary>
    /// 获取或设置字符串属性。
    /// </summary>
    [Id(0)]
    public string? A { get; set; }

    /// <summary>
    /// 获取或设置一个整数属性。
    /// </summary>
    [Id(1)]
    public int B { get; set; }

    /// <summary>
    /// 获取或设置一个长的财产。
    /// </summary>
    [Id(2)]
    public long C { get; set; }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return Equals(obj as TestState1);
    }

    /// <inheritdoc/>
    public bool Equals(TestState1? other)
    {
        if (other is null)
        {
            return false;
        }

        return EqualityComparer<string>.Default.Equals(A, other.A) && B.Equals(other.B) && C.Equals(other.C);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(A, B, C);
    }
}
