using System;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 将 grain 构建参数指定为交易状态表。
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
        /// <param name="stateName">交易国名称。</param>
        /// <param name="storageName">交易状态所用存储供应商的名称。</param>
        public TransactionalStateAttribute(string stateName, string? storageName = null)
        {
            this.StateName = stateName;
            this.StorageName = storageName;
        }
    }
}
