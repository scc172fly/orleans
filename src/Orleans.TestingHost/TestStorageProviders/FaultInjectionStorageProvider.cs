
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
    /// 故障注入 Grain 存储选项。
    /// </summary>
    public class FaultInjectionGrainStorageOptions
    {
        /// <summary>
        /// 默认延迟。
        /// </summary>
        public static TimeSpan DEFAULT_LATENCY = TimeSpan.FromMilliseconds(10);

        /// <summary>
        /// 获取或设置施加于存储操作的延迟。
        /// </summary>
        public TimeSpan Latency { get; set; } = DEFAULT_LATENCY;
    }

    /// <summary>
    /// 存储提供程序的故障注入装饰器。允许用户注入存储异常以测试错误处理场景。
    /// </summary>
    public partial class FaultInjectionGrainStorage : IGrainStorage, ILifecycleParticipant<ISiloLifecycle>
    {
        private readonly IGrainStorage realStorageProvider;
        private readonly IGrainFactory grainFactory;
        private readonly ILogger logger;
        private readonly FaultInjectionGrainStorageOptions options;

        /// <summary>
        /// 创建被装饰存储提供程序的默认构造函数。
        /// </summary>
        /// <param name="realStorageProvider">实际存储提供程序。</param>
        /// <param name="name">存储提供程序名称。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        /// <param name="grainFactory">Grain 工厂。</param>
        /// <param name="faultInjectionOptions">故障注入选项。</param>
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

        /// <summary>如果提供了异常，则以异常结束；否则调用被装饰的存储提供程序。</summary>
        /// <returns>表示指定 Grain 的状态读取操作完成情况的异步任务。</returns>
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

        /// <summary>如果提供了异常，则以异常结束；否则调用被装饰的存储提供程序。</summary>
        /// <returns>表示指定 Grain 的状态写入操作完成情况的异步任务。</returns>
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

        /// <summary>如果提供了异常，则以异常结束；否则调用被装饰的存储提供程序。</summary>
        /// <returns>表示指定 Grain 的状态清除操作完成情况的异步任务。</returns>
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
    /// 创建 FaultInjectionGrainStorage 的工厂。
    /// </summary>
    public static class FaultInjectionGrainStorageFactory
    {
        /// <summary>
        /// 创建新的 <see cref="FaultInjectionGrainStorage"/> 实例。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="name">存储提供程序名称。</param>
        /// <param name="injectedGrainStorageFactory">注入的 Grain 存储工厂。</param>
        /// <returns>新实例。</returns>
        public static IGrainStorage Create(IServiceProvider services, string name, Func<IServiceProvider, string, IGrainStorage> injectedGrainStorageFactory)
        {
            return new FaultInjectionGrainStorage(injectedGrainStorageFactory(services, name), name, services.GetRequiredService<ILoggerFactory>(), services.GetRequiredService<IGrainFactory>(),
                services.GetRequiredService<IOptionsMonitor<FaultInjectionGrainStorageOptions>>().Get(name));
        }
    }
}
