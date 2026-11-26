using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 更偏喜欢当地安插战略表明, grain 如果 grain
    /// 组群中其他部分尚未活跃,当地主机与该主机兼容。
    /// </summary>
    /// <remarks>
    /// 如果主机与 grain 类型不兼容,或如果 grain 收到一项不兼容的请求,则 grain 将
    /// 随机、兼容的服务器上安装。
    /// </remarks>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class PreferLocalPlacement : PlacementStrategy
    {
        /// <summary>
        /// 获得单吨的例子 这个类。
        /// </summary>
        internal static PreferLocalPlacement Singleton { get; } = new PreferLocalPlacement();
    }
}
