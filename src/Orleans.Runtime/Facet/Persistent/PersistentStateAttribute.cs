using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 为应用此特性的 <see cref="IPersistentState{TState}"/> 构造函数参数指定选项。
    /// </summary>
    /// <seealso cref="System.Attribute" />
    /// <seealso cref="Orleans.IFacetMetadata" />
    /// <seealso cref="Orleans.Runtime.IPersistentStateConfiguration" />
    [AttributeUsage(AttributeTargets.Parameter)]
    public class PersistentStateAttribute : Attribute, IFacetMetadata, IPersistentStateConfiguration
    {
        /// <summary>
        /// 初始化 <see cref="PersistentStateAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="stateName">状态名称。</param>
        /// <param name="storageName">存储提供程序名称。</param>
        public PersistentStateAttribute(string stateName, string? storageName = null)
        {
            ArgumentNullException.ThrowIfNull(stateName);
            this.StateName = stateName;
            this.StorageName = storageName;
        }

        /// <summary>
        /// 获取状态名称。
        /// </summary>
        /// <value>状态名称。</value>
        public string StateName { get; }

        /// <summary>
        /// 获取存储提供程序名称。
        /// </summary>
        /// <value>存储提供程序名称。</value>
        public string? StorageName { get; }
    }
}
