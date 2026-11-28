using System;
using Orleans.Core;
using Orleans.Timers;

namespace Orleans.Runtime;

/// <summary>
/// <see cref="Grain"/> 与 Orleans 运行时交互的入口。<see cref="Grain"/> 应仅通过此接口与运行时交互。
/// </summary>
public interface IGrainRuntime
{
    /// <summary>
    /// 获取当前 Silo 的唯一标识符。
    /// 此字符串本身不具有语义含义，但可用于日志记录。
    /// </summary>
    string SiloIdentity { get; }

    /// <summary>
    /// 获取与此实例关联的 Silo 地址。
    /// </summary>
    SiloAddress SiloAddress { get; }

    /// <summary>
    /// 获取 Grain 工厂。
    /// </summary>
    IGrainFactory GrainFactory { get; }

    /// <summary>
    /// 获取定时器注册表。
    /// </summary>
    ITimerRegistry TimerRegistry { get; }

    /// <summary>
    /// 获取服务提供程序。
    /// </summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// 获取时间提供程序。
    /// </summary>
    TimeProvider TimeProvider => TimeProvider.System;

    /// <summary>
    /// 在指定 Grain 进入空闲状态时停用它。
    /// </summary>
    /// <param name="grainContext">Grain 上下文。</param>
    void DeactivateOnIdle(IGrainContext grainContext);

    /// <summary>
    /// 延迟因不活动而对指定 Grain 的激活实例执行的空闲回收，至少等待指定时间。
    /// </summary>
    /// <param name="grainContext">Grain 上下文。</param>
    /// <param name="timeSpan">延迟空闲回收的时长。</param>
    void DelayDeactivation(IGrainContext grainContext, TimeSpan timeSpan);

    /// <summary>
    /// 获取指定 Grain 的存储。
    /// </summary>
    /// <typeparam name="TGrainState">Grain 状态类型。</typeparam>
    /// <param name="grainContext">Grain 上下文。</param>
    /// <returns>指定 Grain 的存储。</returns>
    IStorage<TGrainState> GetStorage<TGrainState>(IGrainContext grainContext);
}
