using System.Collections.Generic;

namespace Orleans.Streams
{
    /// <summary>
    /// 访问部署配置的接口。
    /// </summary>
    public interface IDeploymentConfiguration
    {
        /// <summary>
        /// 获取 silo 全部配置的如实例名 silos.
        /// </summary>
        /// <returns> silo 名称列表。</returns>
        IList<string> GetAllSiloNames();
    }
}
