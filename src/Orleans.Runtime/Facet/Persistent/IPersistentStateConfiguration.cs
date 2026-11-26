
namespace Orleans.Runtime
{
    /// <summary>
    /// 持续状态配置 。
    /// </summary>
    /// <seealso cref="IPersistentState{TState}"/>
    public interface IPersistentStateConfiguration
    {
        /// <summary>
        /// 得到国家的名字。
        /// </summary>
        /// <value>国家名称。</value>
        string StateName { get; }

        /// <summary>
        /// 获取存储供应商的名称。
        /// </summary>
        /// <value>存储提供者的名称。</value>
        string? StorageName { get; }
    }
}
