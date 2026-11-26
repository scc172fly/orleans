
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 错误注入 grain 存储选项
    /// </summary>
    public class FaultInjectionGrainStorageOptions
    {
        /// <summary>
        /// 默认的延迟时间 。
        /// </summary>
        public static TimeSpan DEFAULT_LATENCY = TimeSpan.FromMilliseconds(10);

        /// <summary>
        /// 获取或设置储存操作所用的潜伏。
        /// </summary>
        public TimeSpan Latency { get; set; } = DEFAULT_LATENCY;
    }

    /// <summary>
    /// 存储提供者的错误注入设计器。 这样用户可以将存储例外输入到测试错误处理方案 。
    /// </summary>
    public partial class FaultInjectionGrainStorage : IGrainStorage, ILifecycleParticipant<ISiloLifecycle>
    {
        private readonly IGrainStorage realStorageProvider;
        private readonly IGrainFactory grainFactory;
        private readonly ILogger logger;
        private readonly FaultInjectionGrainStorageOptions options;

        /// <summary>
        /// 创建装饰存储提供者的默认构建器。
        /// </summary>
        /// <param name="realStorageProvider">真实储存提供者。</param>
        /// <param name="name">存储提供者的名称。</param>
        /// <param name="loggerFactory">伐木厂</param>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="faultInjectionOptions">错误注射选项。</param>
        public FaultInjectionGrainStorage(IGrainStorage realStorageProvider, string name, ILoggerFactory loggerFactory,
            IGrainFactory grainFactory, FaultInjectionGrainStorageOptions faultInjectionOptions)
        {
            this.realStorageProvider = realStorageProvider;
            this.logger = loggerFactory.CreateLogger($"{this.GetType().FullName}.{name}");
            this.grainFactory = grainFactory;
            this.options = faultInjectionOptions;
        }

        private Task InsertDelay()
        {
            return Task.Delay(this.options.Latency);
        }

        /// <summary>如果规定了例外,则有过失,否则通过电话呼叫装饰式的储存供应商。</summary>
        /// <returns>特定 grain的读化操作的完成承诺。</returns>
        public async Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            IStorageFaultGrain faultGrain = grainFactory.GetGrain<IStorageFaultGrain>(grainType);
            try
            {
                await InsertDelay();
                await faultGrain.OnRead(grainId);
            }
            catch (Exception)
            {
                LogInformationFaultInjectedForReadState(logger, grainId, grainType);
                throw;
            }
            LogInformationReadState(logger, grainId, grainType);
            await realStorageProvider.ReadStateAsync(grainType, grainId, grainState);
        }

        /// <summary>如果规定了例外,则有过失,否则通过电话呼叫装饰式的储存供应商。</summary>
        /// <returns>指定写入操作的完成承诺 grain.</returns>
        public async Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            IStorageFaultGrain faultGrain = grainFactory.GetGrain<IStorageFaultGrain>(grainType);
            try
            {
                await InsertDelay();
                await faultGrain.OnWrite(grainId);
            }
            catch (Exception)
            {
                LogInformationFaultInjectedForWriteState(logger, grainId, grainType);
                throw;
            }
            LogInformationWriteState(logger, grainId, grainType);
            await realStorageProvider.WriteStateAsync(grainType, grainId, grainState);
        }

        /// <summary>如果规定了例外,则有过失,否则通过电话呼叫装饰式的储存供应商。</summary>
        /// <returns>特定 grain的删除操作的完成承诺。</returns>
        public async Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            IStorageFaultGrain faultGrain = grainFactory.GetGrain<IStorageFaultGrain>(grainType);
            try
            {
                await InsertDelay();
                await faultGrain.OnClear(grainId);
            }
            catch (Exception)
            {
                LogInformationFaultInjectedForClearState(logger, grainId, grainType);
                throw;
            }

            LogInformationClearState(logger, grainId, grainType);
            await realStorageProvider.ClearStateAsync(grainType, grainId, grainState);
        }

        /// <inheritdoc />
        public void Participate(ISiloLifecycle lifecycle)
        {
            (realStorageProvider as ILifecycleParticipant<ISiloLifecycle>)?.Participate(lifecycle);
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Fault injected for ReadState for grain {GrainId} of type {GrainType}"
        )]
        private static partial void LogInformationFaultInjectedForReadState(ILogger logger, GrainId grainId, string grainType);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "ReadState for grain {GrainId} of type {GrainType}"
        )]
        private static partial void LogInformationReadState(ILogger logger, GrainId grainId, string grainType);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Fault injected for WriteState for grain {GrainId} of type {GrainType}"
        )]
        private static partial void LogInformationFaultInjectedForWriteState(ILogger logger, GrainId grainId, string grainType);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "WriteState for grain {GrainId} of type {GrainType}"
        )]
        private static partial void LogInformationWriteState(ILogger logger, GrainId grainId, string grainType);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Fault injected for ClearState for grain {GrainId} of type {GrainType}"
        )]
        private static partial void LogInformationFaultInjectedForClearState(ILogger logger, GrainId grainId, string grainType);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "ClearState for grain {GrainId} of type {GrainType}"
        )]
        private static partial void LogInformationClearState(ILogger logger, GrainId grainId, string grainType);
    }

    /// <summary>
    /// 要创建 FaultInjectionGrainStorage的工厂
    /// </summary>
    public static class FaultInjectionGrainStorageFactory
    {
        /// <summary>
        /// 创建新的 <see cref="FaultInjectionGrainStorage"/> 实例。
        /// </summary>
        /// <param name="services">服务。</param>
        /// <param name="name">存储提供者的名称。</param>
        /// <param name="injectedGrainStorageFactory">注射 grain 储存厂。</param>
        /// <returns>新实例。</returns>
        public static IGrainStorage Create(IServiceProvider services, string name, Func<IServiceProvider, string, IGrainStorage> injectedGrainStorageFactory)
        {
            return new FaultInjectionGrainStorage(injectedGrainStorageFactory(services, name), name, services.GetRequiredService<ILoggerFactory>(), services.GetRequiredService<IGrainFactory>(),
                services.GetRequiredService<IOptionsMonitor<FaultInjectionGrainStorageOptions>>().Get(name));
        }
    }
}
