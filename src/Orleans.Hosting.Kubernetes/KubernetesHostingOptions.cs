using k8s;
using System;

namespace Orleans.Hosting.Kubernetes
{
    /// <summary>
    /// 在 Kubernetes 中托管的选项。
    /// </summary>
    public sealed class KubernetesHostingOptions
    {
        private readonly Lazy<KubernetesClientConfiguration> _clientConfiguration;

        /// <summary>
        /// 用于指定此集群所有 Silo 所属 Kubernetes 命名空间的环境变量。
        /// </summary>
        public const string PodNamespaceEnvironmentVariable = "POD_NAMESPACE";

        /// <summary>
        /// 用于指定此 Silo 所运行的 Kubernetes Pod 名称的环境变量。
        /// </summary>
        public const string PodNameEnvironmentVariable = "POD_NAME";

        /// <summary>
        /// 用于指定此 Pod 的 IP 地址的环境变量。
        /// </summary>
        public const string PodIPEnvironmentVariable = "POD_IP";

        /// <summary>
        /// 用于指定 <see cref="Orleans.Configuration.ClusterOptions.ClusterId"/> 的环境变量。
        /// </summary>
        public const string ClusterIdEnvironmentVariable = "ORLEANS_CLUSTER_ID";

        /// <summary>
        /// 用于指定 <see cref="Orleans.Configuration.ClusterOptions.ServiceId"/> 的环境变量。
        /// </summary>
        public const string ServiceIdEnvironmentVariable = "ORLEANS_SERVICE_ID";

        /// <summary>
        /// Pod 上 <see cref="Orleans.Configuration.ClusterOptions.ServiceId"/> 标签的名称。
        /// </summary>
        public const string ServiceIdLabel = "orleans/serviceId";

        /// <summary>
        /// Pod 上 <see cref="Orleans.Configuration.ClusterOptions.ClusterId"/> 标签的名称。
        /// </summary>
        public const string ClusterIdLabel = "orleans/clusterId";

        /// <summary>
        /// 初始化 <see cref="KubernetesHostingOptions"/> 类的新实例。
        /// </summary>
        public KubernetesHostingOptions()
        {
            _clientConfiguration = new Lazy<KubernetesClientConfiguration>(() => this.GetClientConfiguration());
        }

        /// <summary>
        /// 获取客户端配置。
        /// </summary>
        internal KubernetesClientConfiguration ClientConfiguration => _clientConfiguration.Value;

        /// <summary>
        /// 用于获取 <see cref="KubernetesClientConfiguration"/> 实例的委托。
        /// </summary>
        public Func<KubernetesClientConfiguration> GetClientConfiguration { get; set; } = KubernetesClientConfiguration.InClusterConfig;

        /// <summary>
        /// 集群中应监视 Kubernetes 的 Silo 数量。
        /// </summary>
        /// <remarks>
        /// 将此值设为较小的数字，可以降低 Kubernetes API 服务器的负载。
        /// </remarks>
        public int MaxAgents { get; set; } = 2;

        /// <summary>
        /// 获取或设置 Kubernetes API 调用的最大重试尝试次数。
        /// </summary>
        public int MaxKubernetesApiRetryAttempts { get; set; } = 10;

        /// <summary>
        /// 是否删除与此 Silo 激活后已失效的 Silo 对应的 Pod。
        /// </summary>
        public bool DeleteDefunctSiloPods { get; set; } = false;

        /// <summary>
        /// 此 Silo 与其他所有 Silo 所属的 Kubernetes 命名空间。
        /// </summary>
        internal string? Namespace { get; set; }

        /// <summary>
        /// 此 Silo 所运行的 Kubernetes Pod 名称。
        /// </summary>
        internal string? PodName { get; set; }

        /// <summary>
        /// 此 Silo 所运行的 Kubernetes Pod 的 PodIP。
        /// </summary>
        internal string? PodIP { get; set; }
    }
}
