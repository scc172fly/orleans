using System.Diagnostics.CodeAnalysis;
using Orleans.Metadata;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 选择 grain 类型中用于激活范围宽度耐用状态管理器的名为日记存储提供者。
/// </summary>
/// <param name="providerName">注册的日记存储提供者姓名。</param>
/// <remarks>
/// 激活时所有耐用状态都共享选中的提供者。 Grain 类型不使用此属性
/// <see cref="Providers.ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME"/>.
/// 回收读取选定提供者的物理命名空间。 更改现有日记的选择
/// 数据迁移或切换战略。
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class JournalStorageProviderAttribute(string providerName) : Attribute, IGrainPropertiesProviderAttribute
{
    internal const string PropertyKey = "journal-storage-provider";

    /// <summary>
    /// 获得注册日记存储供应商的名字。
    /// </summary>
    public string ProviderName { get; } = providerName;

    /// <inheritdoc />
    /// <exception cref="OrleansConfigurationException">服务商名称为空、空或空白。</exception>
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
