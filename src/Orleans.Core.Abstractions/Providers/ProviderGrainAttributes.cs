using System;

namespace Orleans.Providers
{
    /// <summary>
    /// Orleans.Providers.StorageProvider属性用于确定哪些储存供应商可用于 grain 状态的持久性。
    /// <para>
    /// 指定[Orleans.Providers.StorageProvider......财产是建议所有各方 grains 延伸 Grain&lt;T&gt;.
    /// 如果未指定 [Orleans.Providers.StorageProvider] 属性,则将使用“默认”存储提供者。
    /// 如果无法找到合适的储存提供商 grain,然后 grain 无法装入 Silo.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class StorageProviderAttribute : Attribute
    {
        /// <summary>
        /// 获取或设置用于持续状态 grain 的提供者的名称。
        /// </summary>
        public string ProviderName { get; set; }

        /// <summary>
        /// 初始化 <see cref="StorageProviderAttribute"/> 类的新实例。
        /// </summary>
        public StorageProviderAttribute()
        {
            ProviderName = ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME;
        }
    }

    /// <summary>
    /// Orleans.Providers.LogConsistencyProvider属性用于界定使用日志状态抽象法在 grains 中使用的哪个一致性提供者。
    /// <para>
    /// 指定[Orleans.Providers.LogConsistencyProvider......财产是建议所有各方 grains 产生
    /// LogConsistentGrain,例如 JournaledGrain。
    /// 如果未指定 [Orleans.Providers.LogConsistencyProvider]属性,运行时间将尝试查找
    /// 如下一一。首先,它寻找
    /// 配置文件中的“ 默认” 提供者, 然后检查 grain 类型是否定义了默认值 。
    /// 如果无法找到该 grain的一致提供商,则 grain 将无法装入 Silo。
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class LogConsistencyProviderAttribute : Attribute
    {
        /// <summary>
        /// 用于一致性的提供者的组名或组名。
        /// </summary>
        public string ProviderName { get; set; }

        /// <summary>
        /// 初始化 <see cref="LogConsistencyProviderAttribute"/> 类的新实例。
        /// </summary>
        public LogConsistencyProviderAttribute()
        {
            ProviderName = ProviderConstants.DEFAULT_LOG_CONSISTENCY_PROVIDER_NAME;
        }
    }
}
