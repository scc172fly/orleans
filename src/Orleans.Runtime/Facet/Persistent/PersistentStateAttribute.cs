using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 指定用于 <see cref="IPersistentState{TState}"/> 构建器参数的选项。
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
        /// <param name="stateName">国家名称。</param>
        /// <param name="storageName">存储提供者的名称。</param>
        public PersistentStateAttribute(string stateName, string? storageName = null)
        {
            ArgumentNullException.ThrowIfNull(stateName);
            this.StateName = stateName;
            this.StorageName = storageName;
        }

        /// <summary>
        /// 得到国家的名字。
        /// </summary>
        /// <value>国家名称。</value>
        public string StateName { get; }

        /// <summary>
        /// 获取存储供应商的名称。
        /// </summary>
        /// <value>存储提供者的名称。</value>
        public string? StorageName { get; }
    }
}
