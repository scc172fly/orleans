using System.Diagnostics.CodeAnalysis;
using Orleans.Metadata;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 选择 Grain 类型的激活作用域持久化状态管理器使用的命名日志存储提供程序。
/// </summary>
/// <param name="providerName">已注册日志存储提供程序的名称。</param>
/// <remarks>
/// 激活实例中的全部持久化状态共享选定的提供程序。不具有此特性的 Grain 类型使用
/// <see cref="Providers.ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME"/>.
/// 恢复读取选定提供程序的物理命名空间。改变已有日志的提供程序选择
/// 需要数据迁移或切换策略。
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class JournalStorageProviderAttribute(string providerName) : Attribute, IGrainPropertiesProviderAttribute
{
    internal const string PropertyKey = "journal-storage-provider";

    /// <summary>
    /// 获取已注册日志存储提供程序的名称。
    /// </summary>
    public string ProviderName { get; } = providerName;

    /// <inheritdoc />
    /// <exception cref="OrleansConfigurationException">提供程序名称为 null、空字符串或仅包含空白字符。</exception>
    [SuppressMessage("Design", "CA1062:Validate arguments of public methods",
        Justification = "AttributeGrainPropertiesProvider validates the grain class and properties collection before dispatching to attributes.")]
    public void Populate(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
    {
        if (string.IsNullOrWhiteSpace(ProviderName))
        {
            throw new OrleansConfigurationException(
                $"Journal storage provider '{ProviderName}' for grain type '{grainClass.FullName}' ('{grainType}') must have a non-empty name.");
        }

        properties[PropertyKey] = ProviderName;
    }
}
