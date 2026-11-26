using Xunit.v3;

/// <summary>
/// 提供测试套件时遗留 xUnit <c>Category</c> 特性的兼容性桥桥
/// 向头等舱 <see cref="TestSuiteAttribute"/>、 <see cref="TestProviderAttribute"/>和
/// <see cref="TestAreaAttribute"/> 特征。
/// </summary>
/// <remarks>
/// 新的试验应更倾向于第一等特征。
/// <c>[TestCategory("BVT")]</c> 直到他们迁移。
/// </remarks>
/// <example>
/// <code>
/// [TestCategory("BVT")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class TestCategoryAttribute : Attribute, ITraitAttribute
{
    public TestCategoryAttribute(string category)
    {
        Category = category;
    }

    public string Category { get; }

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [new(TestTraitNames.Category, Category)];
}
