using Xunit.v3;

internal static class TestTraitNames
{
    public const string Area = "Area";
    public const string Category = "Category";
    public const string Provider = "Provider";
    public const string Suite = "Suite";
}

/// <summary>
/// 测试标记为属于CI组套房
/// </summary>
/// <remarks>
/// 预期值包括标准CI的 <c>BVT</c>、 <c>SlowBVT</c>和 <c>Functional</c> ,
/// 加非标准套件,如 <c>Nightly</c> 和 <c>Benchmark</c>。
/// </remarks>
/// <example>
/// <code>
/// [TestSuite("BVT")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class TestSuiteAttribute : Attribute, ITraitAttribute
{
    public TestSuiteAttribute(string suite)
    {
        Suite = suite;
    }

    public string Suite { get; }

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [new(TestTraitNames.Suite, Suite)];
}

/// <summary>
/// 标记一个测试,使用它的后端或后端提供支援。
/// </summary>
/// <remarks>
/// 没有外部提供者, 测试使用 <c>None</c> 。
/// </remarks>
/// <example>
/// <code>
/// [TestProvider("None")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class TestProviderAttribute : Attribute, ITraitAttribute
{
    public TestProviderAttribute(string provider)
    {
        Provider = provider;
    }

    public string Provider { get; }

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [new(TestTraitNames.Provider, Provider)];
}

/// <summary>
/// 标记信息功能区域测试。
/// </summary>
/// <example>
/// <code>
/// [TestArea("Streaming")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class TestAreaAttribute : Attribute, ITraitAttribute
{
    public TestAreaAttribute(string area)
    {
        Area = area;
    }

    public string Area { get; }

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [new(TestTraitNames.Area, Area)];
}
