using System;
using System.Diagnostics.CodeAnalysis;
using Orleans.Concurrency;

namespace Orleans.Runtime;

/// <summary>
/// 用于请求的选项,其中 grain 队列使用
/// <see cref="IGrainContext.ReceiveLocalRequest"/> 或 <see cref="GrainBaseExtensions"/>的 <c>RunRequestAsync</c> 方法。
/// </summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Per-call options are passed to the runtime rather than compared.")]
public readonly struct LocalRequestOptions
{
    // 被倒转存储, 以便默认(LocalRequestOptions) -- -- 调用者作为默认参数通过 -- -- 保持激活的活力。
    private readonly bool _suppressKeepAlive;

    /// <summary>
    /// 初始化带有默认选项的新 <see cref="LocalRequestOptions"/> 例。
    /// </summary>
    public LocalRequestOptions()
    {
    }

    /// <summary>
    /// 获得一个值,以显示请求是否延长 grain 激活的寿命。
    /// 默认为 <see langword="true"/>。
    /// </summary>
    /// <remarks>
    /// 请求激活活动能保持生命, 请求在启动完成时重新启动空闲的收集期,
    /// 与 grain 呼叫相同。不保持激活活性的请求使闲置的收集期保持不变。
    /// </remarks>
    public bool KeepAlive
    {
        get => !_suppressKeepAlive;
        init => _suppressKeepAlive = !value;
    }

    /// <summary>
    /// 获得一个值,显示请求是否可与其他启动请求发生关联。
    /// 默认为 <see langword="false"/>。
    /// </summary>
    /// <remarks>
    /// 如果该数值是 <see langword="false"/>,则请求遵循 grain的再恢复规则,与 grain 调用相同。
    /// 如果数值是 <see langword="true"/>,则请求与其他请求相互串连,与要求使用
    /// 适用 <see cref="AlwaysInterleaveAttribute"/> 。
    /// </remarks>
    public bool Interleave { get; init; }
}
