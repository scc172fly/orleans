using System;
using Orleans.Core;
using Orleans.Timers;

namespace Orleans.Runtime;

/// <summary>
/// <see cref="Grain"/> 至 Orleans 运行时间的网关。 <see cref="Grain"/> 仅应在此界面中与运行时间进行互动。
/// </summary>
public interface IGrainRuntime
{
    /// <summary>
    /// 获取当前 silo的独特标识符。
    /// 此字符串没有语义内容, 但可能对记录有用 。
    /// </summary>
    string SiloIdentity { get; }

    /// <summary>
    /// 获取与此实例相关的 silo 地址。
    /// </summary>
    SiloAddress SiloAddress { get; }

    /// <summary>
    /// 得到 grain 工厂。
    /// </summary>
    IGrainFactory GrainFactory { get; }

    /// <summary>
    /// 获得计时器登记簿。
    /// </summary>
    ITimerRegistry TimerRegistry { get; }

    /// <summary>
    /// 得到了服务供应商。
    /// </summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// 时间提供商。
    /// </summary>
    TimeProvider TimeProvider => TimeProvider.System;

    /// <summary>
    /// 取消提供 grain 当它闲着的时候,
    /// </summary>
    /// <param name="grainContext"> grain 上下文。</param>
    void DeactivateOnIdle(IGrainContext grainContext);

    /// <summary>
    /// 延迟闲置启动所提供设备 grain 直至至少规定时间结束。
    /// </summary>
    /// <param name="grainContext"> grain 上下文。</param>
    /// <param name="timeSpan">延迟闲置激活收藏的时间。</param>
    void DelayDeactivation(IGrainContext grainContext, TimeSpan timeSpan);

    /// <summary>
    /// 获取 grain 提供的储存 grain.
    /// </summary>
    /// <typeparam name="TGrainState"> grain 状态类型。</typeparam>
    /// <param name="grainContext"> grain 上下文。</param>
    /// <returns>下 grain 提供的储存 grain.</returns>
    IStorage<TGrainState> GetStorage<TGrainState>(IGrainContext grainContext);
}
