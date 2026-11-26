using System;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 将 grain 构建参数指定为交易承诺方面板。
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class TransactionCommitterAttribute : Attribute, IFacetMetadata, ITransactionCommitterConfiguration
    {
        /// <inheritdoc />
        public string ServiceName { get; }

        /// <inheritdoc />
        public string? StorageName { get; }

        /// <summary>
        /// 初始化 <see cref="TransactionCommitterAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="serviceName">接受已承诺行动的机构的名称。</param>
        /// <param name="storageName">交易承诺方使用的存储提供者的名称。</param>
        public TransactionCommitterAttribute(string serviceName, string? storageName = null)
        {
            this.ServiceName = serviceName;
            this.StorageName = storageName;
        }
    }
}
