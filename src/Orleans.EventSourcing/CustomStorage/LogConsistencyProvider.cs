using Orleans.Storage;
using Orleans.Configuration;
using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.EventSourcing.CustomStorage
{
    /// <summary>
    /// 依赖特定于 Grain 的自定义代码的日志一致性提供程序，
    /// 该代码负责从存储读取状态并向存储追加增量。
    /// 希望使用此提供程序的 Grain 必须实现 <see cref="ICustomStorageInterface{TState, TDelta}"/>
    /// 接口，以定义如何读取状态及写入增量。
    /// 配置的主集群标识符会传递给每个自定义存储适配器。
    /// 自定义存储适配器接受来自所有集群的提交。
    /// </summary>
    public class LogConsistencyProvider : ILogViewAdaptorFactory
    {
        private readonly CustomStorageLogConsistencyOptions options;

        /// <summary>
        /// 获取传递给每个自定义存储适配器的集群标识符。
        /// </summary>
        public string? PrimaryCluster => options.PrimaryCluster;

        /// <inheritdoc/>
        public bool UsesStorageProvider => false;

        /// <summary>
        /// 初始化 <see cref="LogConsistencyProvider"/> 类的新实例。
        /// </summary>
        /// <param name="options">提供程序配置。</param>
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
    /// 根据命名选项创建自定义存储日志一致性提供程序。
    /// </summary>
    public static class LogConsistencyProviderFactory
    {
        /// <summary>
        /// 创建自定义存储日志一致性提供程序。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="name">提供程序配置的名称。</param>
        /// <returns>已配置的日志视图适配器工厂。</returns>
        public static ILogViewAdaptorFactory Create(IServiceProvider services, string? name)
        {
            var optionsMonitor = services.GetRequiredService<IOptionsMonitor<CustomStorageLogConsistencyOptions>>();
            return ActivatorUtilities.CreateInstance<LogConsistencyProvider>(services, optionsMonitor.Get(name));
        }
    }
}
