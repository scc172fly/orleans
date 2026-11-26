using System;

using Microsoft.Extensions.DependencyInjection;
using Orleans.Messaging;
using Orleans.Runtime;

namespace Orleans.Configuration.Validators
{
    /// <summary>
    /// 客户端集群校验器
    /// </summary>
    internal class ClientClusteringValidator : IConfigurationValidator
    {
        /// <summary>
        /// 组合错误配置时显示错误消息 。
        /// </summary>
        internal const string ClusteringNotConfigured =
            "Clustering has not been configured. Configure clustering using one of the clustering packages, such as:"
            + "\n  * Microsoft.Orleans.Clustering.AzureStorage"
            + "\n  * Microsoft.Orleans.Clustering.AdoNet for ADO.NET systems such as SQL Server, MySQL, PostgreSQL, and Oracle"
            + "\n  * Microsoft.Orleans.Clustering.DynamoDB"
            + "\n  * Microsoft.Orleans.Clustering.Consul"
            + "\n  * Microsoft.Orleans.Clustering.ZooKeeper"
            + "\n  * Others, see: https://www.nuget.org/packages?q=Microsoft.Orleans.Clustering.";

        /// <summary>
        /// 服务供应商。
        /// </summary>
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 初始化 <see cref="ClientClusteringValidator"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">
        /// 服务供应商。
        /// </param>
        public ClientClusteringValidator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public void ValidateConfiguration()
        {
            var gatewayProvider = _serviceProvider.GetService<IGatewayListProvider>();
            if (gatewayProvider == null)
            {
                throw new OrleansConfigurationException(ClusteringNotConfigured);
            }
        }
    }
}
