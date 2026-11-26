using Orleans.Storage;
using Orleans.Configuration;
using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.EventSourcing.CustomStorage
{
    /// <summary>
    /// 依赖 grain- 特定自定义代码的对数一致性提供商 
    /// 正在从存储中读取状态, 并附加三角洲到存储。
    /// 希望使用该供应商的Grains 必须执行 <see cref="ICustomStorageInterface{TState, TDelta}"/>
    /// 界面, 定义状态如何读取和三角洲是如何写成的 。
    /// 配置的主要集束识别码将传送给每个自定义存储适配器。
    /// 自定义存储适应器接受每个组群的提交文件。
    /// </summary>
    public class LogConsistencyProvider : ILogViewAdaptorFactory
    {
        private readonly CustomStorageLogConsistencyOptions options;

        /// <summary>
        /// 将集束识别码传递给每个自定义存储适配器。
        /// </summary>
        public string? PrimaryCluster => options.PrimaryCluster;

        /// <inheritdoc/>
        public bool UsesStorageProvider => false;

        /// <summary>
        /// 初始化 <see cref="LogConsistencyProvider"/> 类的新实例。
        /// </summary>
        /// <param name="options">供应商配置。</param>
        public LogConsistencyProvider(CustomStorageLogConsistencyOptions options)
        {
            this.options = options;
        }

        /// <inheritdoc/>
        public ILogViewAdaptor<TView, TEntry> MakeLogViewAdaptor<TView, TEntry>(ILogViewAdaptorHost<TView, TEntry> hostGrain, TView initialState, string grainTypeName, IGrainStorage? grainStorage, ILogConsistencyProtocolServices services)
            where TView : class, new()
            where TEntry : class
        {
            return new CustomStorageAdaptor<TView, TEntry>(hostGrain, initialState, services, PrimaryCluster);
        }
    }

    /// <summary>
    /// 从指定的选项创建自定义存储日志一致性提供者 。
    /// </summary>
    public static class LogConsistencyProviderFactory
    {
        /// <summary>
        /// 创建自定义存储日志一致性提供者。
        /// </summary>
        /// <param name="services">服务供应商。</param>
        /// <param name="name">供应商配置的名称。</param>
        /// <returns>配置的日志视图适配器厂</returns>
        public static ILogViewAdaptorFactory Create(IServiceProvider services, string? name)
        {
            var optionsMonitor = services.GetRequiredService<IOptionsMonitor<CustomStorageLogConsistencyOptions>>();
            return ActivatorUtilities.CreateInstance<LogConsistencyProvider>(services, optionsMonitor.Get(name));
        }
    }
}
