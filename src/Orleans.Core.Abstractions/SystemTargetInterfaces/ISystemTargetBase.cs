using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 由 SystemTarget 基级实施的内部接口,能够为系统目标生成 grain 参考。
    /// </summary>
    internal interface ISystemTargetBase : IGrainContext
    {
        /// <summary>
        /// 获取此系统目标所激活的服务器地址 。
        /// </summary>
        SiloAddress Silo { get; }
    }
}
