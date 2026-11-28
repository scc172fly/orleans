namespace Orleans.Runtime;

/// <summary>
/// 表示组件存在待处理工作时可以保持活动的激活实例。
/// </summary>
internal interface IActivationKeepAlive
{
    /// <summary>
    /// 阻止激活实例的空闲回收，直至调用与之配对的 <see cref="ReleaseKeepAlive"/>。
    /// </summary>
    /// <remarks>
    /// 保活请求采用计数方式管理。内存压力回收和显式停用不会考虑这些保活请求。
    /// </remarks>
    void AcquireKeepAlive();

    /// <summary>
    /// 释放通过 <see cref="AcquireKeepAlive"/> 获取的保活请求。释放最后一个保活请求时，有效激活实例将重新开始计算空闲时间。
    /// </summary>
    void ReleaseKeepAlive();
}
