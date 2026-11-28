using System;

namespace Orleans.Providers
{
    /// <summary>
    /// [Orleans.Providers.StorageProvider] 特性用于指定持久化 Grain 状态时使用的存储提供程序。
    /// <para>
    /// 建议所有继承 Grain&lt;T&gt; 的 Grain 都指定 [Orleans.Providers.StorageProvider] 特性。
    /// 如果未指定 [Orleans.Providers.StorageProvider] 特性，则使用名为“Default”的存储提供程序。
    /// 如果找不到适用于此 Grain 的存储提供程序，则无法将此 Grain 加载到 Silo 中。
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class StorageProviderAttribute : Attribute
    {
        /// <summary>
        /// 获取或设置用于持久化 Grain 状态的提供程序名称。
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
    /// [Orleans.Providers.LogConsistencyProvider] 特性用于指定采用日志视图状态抽象的 Grain 所使用的一致性提供程序。
    /// <para>
    /// 建议所有继承 LogConsistentGrain 的 Grain（如 JournaledGrain）
    /// 都指定 [Orleans.Providers.LogConsistencyProvider] 特性。
    /// 如果未指定 [Orleans.Providers.LogConsistencyProvider] 特性，运行时将按以下方式尝试查找
    /// 一致性提供程序：首先查找配置文件中名为
    /// “Default”的提供程序，然后检查该 Grain 类型是否定义了默认提供程序。
    /// 如果找不到适用于此 Grain 的一致性提供程序，则无法将此 Grain 加载到 Silo 中。
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class LogConsistencyProviderAttribute : Attribute
    {
        /// <summary>
        /// 获取或设置用于实现一致性的提供程序名称。
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
