using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 由 SystemTarget 基类实现的内部接口，用于为系统目标生成 Grain 引用。
    /// </summary>
    internal interface ISystemTargetBase : IGrainContext
    {
        /// <summary>
        /// 获取激活此系统目标的服务器地址。
        /// </summary>
        SiloAddress Silo { get; }
    }
}
