using System;

namespace Orleans
{
    /// <summary>
    /// 与 Orleans 集进行互动的客户界面。
    /// </summary>
    public interface IClusterClient : IGrainFactory
    {
        /// <summary>
        /// 获取客户所使用的服务供应商。
        /// </summary>
        IServiceProvider ServiceProvider { get; }
    }
}
