using System;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 将 Grain 构造函数参数标识为事务提交器组件（facet）。
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
        /// <param name="serviceName">接收已提交操作的服务名称。</param>
        /// <param name="storageName">事务提交器使用的存储提供程序名称。</param>
        public TransactionCommitterAttribute(string serviceName, string? storageName = null)
        {
            this.ServiceName = serviceName;
            this.StorageName = storageName;
        }
    }
}
