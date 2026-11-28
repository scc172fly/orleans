using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Jobs;
using Orleans.Serialization;
using Orleans.Serialization.TypeSystem;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// Orleans.Durable 中可序列化的类型带有别名，因此序列化器写入日志及作业
/// 存储中的类型名称不包含声明该类型的程序集。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestCategory("BVT")]
public sealed class DurableTypeIdentityTests
{
    [Fact]
    public void EverySerializableTypeHasAnAlias()
    {
        var unaliased = typeof(IDurableStateManager).Assembly.GetTypes()
            .Where(type => type.IsDefined(typeof(GenerateSerializerAttribute), inherit: false)
                && !type.IsDefined(typeof(AliasAttribute), inherit: false))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal);

        Assert.Empty(unaliased);
    }

    [Theory]
    [InlineData(typeof(ValueChange<int>), "Orleans.Durable.Journaling.ValueChange`1[[int]]")]
    [InlineData(typeof(JournaledStatePreCommitException), "Orleans.Durable.Journaling.JournaledStatePreCommitException")]
    [InlineData(typeof(DurableJob), "Orleans.Durable.Jobs.DurableJob")]
    public void FormattedNameIsTheAlias(Type type, string expected)
    {
        var converter = CreateTypeConverter();

        Assert.Equal(expected, converter.Format(type));
        Assert.True(converter.TryParse(expected, out var parsed));
        Assert.Equal(type, parsed);
    }

    private static TypeConverter CreateTypeConverter()
        => new ServiceCollection().AddSerializer().BuildServiceProvider().GetRequiredService<TypeConverter>();
}
