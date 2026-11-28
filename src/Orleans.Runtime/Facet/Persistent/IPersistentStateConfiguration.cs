
namespace Orleans.Runtime
{
    /// <summary>
    /// 持久化状态配置。
    /// </summary>
    /// <seealso cref="IPersistentState{TState}"/>
    public interface IPersistentStateConfiguration
    {
        /// <summary>
        /// 获取状态名称。
        /// </summary>
        /// <value>状态名称。</value>
        string StateName { get; }

        /// <summary>
        /// 获取存储提供程序名称。
        /// </summary>
        /// <value>存储提供程序名称。</value>
        string? StorageName { get; }
    }
}
