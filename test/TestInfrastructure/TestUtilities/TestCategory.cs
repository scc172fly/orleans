using Xunit.v3;

/// <summary>
/// 在测试套件迁移到一等特征的过程中，为旧版 xUnit <c>Category</c> 特征提供兼容桥接，
/// 迁移目标为 <see cref="TestSuiteAttribute"/>、<see cref="TestProviderAttribute"/> 和
/// <see cref="TestAreaAttribute"/> 特征。
/// </summary>
/// <remarks>
/// 新测试应优先使用这些一等特征。现有测试在完成迁移前仍可使用
/// <c>[TestCategory("BVT")]</c>。
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
