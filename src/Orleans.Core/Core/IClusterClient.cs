using System;

namespace Orleans
{
    /// <summary>
    /// 用于与 Orleans 集群交互的客户端接口。
    /// </summary>
    public interface IClusterClient : IGrainFactory
    {
        /// <summary>
        /// 获取此客户端使用的服务提供程序。
        /// </summary>
        IServiceProvider ServiceProvider { get; }
    }
}
