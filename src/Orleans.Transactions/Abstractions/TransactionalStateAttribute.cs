using System;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 将 Grain 构造函数参数标识为事务状态组件（facet）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class TransactionalStateAttribute : Attribute, IFacetMetadata, ITransactionalStateConfiguration
    {
        /// <inheritdoc />
        public string StateName { get; }

        /// <inheritdoc />
        public string? StorageName { get; }

        /// <summary>
        /// 初始化 <see cref="TransactionalStateAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="stateName">事务状态名称。</param>
        /// <param name="storageName">事务状态使用的存储提供程序名称。</param>
        public TransactionalStateAttribute(string stateName, string? storageName = null)
        {
            this.StateName = stateName;
            this.StorageName = storageName;
        }
    }
}
