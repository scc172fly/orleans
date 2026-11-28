using System.Collections.Generic;

namespace Orleans.Streams
{
    /// <summary>
    /// 用于访问部署配置的接口。
    /// </summary>
    public interface IDeploymentConfiguration
    {
        /// <summary>
        /// 获取所有已配置 Silo 的实例名称。
        /// </summary>
        /// <returns>Silo 名称列表。</returns>
        IList<string> GetAllSiloNames();
    }
}
