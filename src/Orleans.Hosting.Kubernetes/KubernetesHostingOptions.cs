using k8s;
using System;

namespace Orleans.Hosting.Kubernetes
{
    /// <summary>
    /// Kubernetes中托管的选项。
    /// </summary>
    public sealed class KubernetesHostingOptions
    {
        private readonly Lazy<KubernetesClientConfiguration> _clientConfiguration;

        /// <summary>
        /// 用于指定 Kubernetes 全部 silos 本组中属于该组。
        /// </summary>
        public const string PodNamespaceEnvironmentVariable = "POD_NAMESPACE";

        /// <summary>
        /// 指定 Kubernetes 哪个此缓放 silo 执行中。
        /// </summary>
        public const string PodNameEnvironmentVariable = "POD_NAME";

        /// <summary>
        /// 指定此播客的 IP 地址的环境变量 。
        /// </summary>
        public const string PodIPEnvironmentVariable = "POD_IP";

        /// <summary>
        /// 指定 <see cref="Orleans.Configuration.ClusterOptions.ClusterId"/>的环境变量。
        /// </summary>
        public const string ClusterIdEnvironmentVariable = "ORLEANS_CLUSTER_ID";

        /// <summary>
        /// 指定 <see cref="Orleans.Configuration.ClusterOptions.ServiceId"/>的环境变量。
        /// </summary>
        public const string ServiceIdEnvironmentVariable = "ORLEANS_SERVICE_ID";

        /// <summary>
        /// 播客舱上的 <see cref="Orleans.Configuration.ClusterOptions.ServiceId"/> 标签的名称。
        /// </summary>
        public const string ServiceIdLabel = "orleans/serviceId";

        /// <summary>
        /// 播客舱上的 <see cref="Orleans.Configuration.ClusterOptions.ClusterId"/> 标签的名称。
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
        /// 获得客户配置。
        /// </summary>
        internal KubernetesClientConfiguration ClientConfiguration => _clientConfiguration.Value;

        /// <summary>
        /// 该代表过去曾得到过 <see cref="KubernetesClientConfiguration"/>的例子。
        /// </summary>
        public Func<KubernetesClientConfiguration> GetClientConfiguration { get; set; } = KubernetesClientConfiguration.InClusterConfig;

        /// <summary>
        /// 编号数 silos 组内应监测 Kubernetes.
        /// </summary>
        /// <remarks>
        /// 设置此选项为小数, 将减少 Kubernetes API 服务器的负载 。
        /// </remarks>
        public int MaxAgents { get; set; } = 2;

        /// <summary>
        /// 获取或设定尝试重试 Kubernetes API 电话的最大次数。
        /// </summary>
        public int MaxKubernetesApiRetryAttempts { get; set; } = 10;

        /// <summary>
        /// 是否删除对应的 silos 之后已失效的 silo 活动。
        /// </summary>
        public bool DeleteDefunctSiloPods { get; set; } = false;

        /// <summary>
        /// 下 Kubernetes 哪个命名空间 silo 和所有其他其他 silos 属于.
        /// </summary>
        internal string? Namespace { get; set; }

        /// <summary>
        /// 名称 Kubernetes 哪个此缓放 silo 执行中。
        /// </summary>
        internal string? PodName { get; set; }

        /// <summary>
        /// 下 PodIP 会 议 排排排排排排排排排排排排排排排排排 Kubernetes 哪个此缓放 silo 执行中。
        /// </summary>
        internal string? PodIP { get; set; }
    }
}
