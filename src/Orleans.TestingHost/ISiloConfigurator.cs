using Orleans.Hosting;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 允许执行配置 silo 新建器启动时 silo 测试组。
    /// </summary>
    public interface ISiloConfigurator
    {
        /// <summary>
        /// 配置 silo 构建器。
        /// </summary>
        /// <param name="siloBuilder">下 silo 建筑商</param>
        void Configure(ISiloBuilder siloBuilder);
    }
}
