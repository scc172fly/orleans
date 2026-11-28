using System;
using System.Diagnostics.CodeAnalysis;
using Orleans.Concurrency;

namespace Orleans.Runtime;

/// <summary>
/// Grain 将请求加入自身激活实例的队列时使用的选项，适用于
/// <see cref="IGrainContext.ReceiveLocalRequest"/> 或 <see cref="GrainBaseExtensions"/> 的 <c>RunRequestAsync</c> 方法。
/// </summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Per-call options are passed to the runtime rather than compared.")]
public readonly struct LocalRequestOptions
{
    // 以取反形式存储，使调用方作为默认参数传入的 default(LocalRequestOptions) 保持激活实例活动。
    private readonly bool _suppressKeepAlive;

    /// <summary>
    /// 使用默认选项初始化一个新的 <see cref="LocalRequestOptions"/> 实例。
    /// </summary>
    public LocalRequestOptions()
    {
    }

    /// <summary>
    /// 获取一个值，指示请求是否延长 Grain 激活实例的生存期。
    /// 默认值为 <see langword="true"/>。
    /// </summary>
    /// <remarks>
    /// 保持激活实例活动的请求在完成时会重新开始计算其空闲回收时间，
    /// 与 Grain 调用相同。不保持激活实例活动的请求不会改变其空闲回收时间。
    /// </remarks>
    public bool KeepAlive
    {
        get => !_suppressKeepAlive;
        init => _suppressKeepAlive = !value;
    }

    /// <summary>
    /// 获取一个值，指示请求是否可以与此激活实例上的其他请求交错执行。
    /// 默认值为 <see langword="false"/>。
    /// </summary>
    /// <remarks>
    /// 如果此值为 <see langword="false"/>，请求将遵循 Grain 的可重入规则，与普通 Grain 调用相同。
    /// 如果此值为 <see langword="true"/>，此请求将与其他请求交错执行，其行为与调用标记了
    /// <see cref="AlwaysInterleaveAttribute"/> 的方法相同。
    /// </remarks>
    public bool Interleave { get; init; }
}
