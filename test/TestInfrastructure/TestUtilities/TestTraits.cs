using Xunit.v3;

internal static class TestTraitNames
{
    public const string Area = "Area";
    public const string Category = "Category";
    public const string Provider = "Provider";
    public const string Suite = "Suite";
}

/// <summary>
/// 标记测试所属的 CI 套件。
/// </summary>
/// <remarks>
/// 标准 CI 的预期值包括 <c>BVT</c>、<c>SlowBVT</c> 和 <c>Functional</c>，
/// 非标准套件则包括 <c>Nightly</c> 和 <c>Benchmark</c> 等。
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
/// 标记测试使用的提供程序或后端。
/// </summary>
/// <remarks>
/// 对于不使用外部提供程序的测试，使用 <c>None</c>。
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
/// 标记测试的功能领域，仅供参考。
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
