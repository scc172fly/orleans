using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 Orleans 集群。
    /// </summary>
    public class ClusterOptions
    {
        /// <summary>
        /// <see cref="ClusterId"/> 的默认值。
        /// </summary>
        public const string DefaultClusterId = "default";

        /// <summary>
        /// <see cref="ServiceId"/> 的默认值。
        /// </summary>
        public const string DefaultServiceId = "default";

        /// <summary>
        /// 开发集群的默认集群 ID。
        /// </summary>
        internal const string DevelopmentClusterId = "dev";

        /// <summary>
        /// 开发集群的默认服务 ID。
        /// </summary>
        internal const string DevelopmentServiceId = "dev";

        /// <summary>
        /// 获取或设置集群标识。Orleans 2.0 之前称为 DeploymentId。
        /// </summary>
        public string ClusterId { get; set; } = DefaultClusterId;

        /// <summary>
        /// 获取或设置此服务的唯一标识符；它应在部署与重新部署之间保持不变，而 <see cref="ClusterId"/> 则可能变化。
        /// </summary>
        public string ServiceId { get; set; } = DefaultServiceId;
    }

    /// <summary>
    /// <see cref="ClusterOptions"/> 的验证器。
    /// </summary>
    public class ClusterOptionsValidator : IConfigurationValidator
    {
        private readonly ClusterOptions options;

        /// <summary>
        /// 初始化 <see cref="ClusterOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">
        /// 选项。
        /// </param>
        public ClusterOptionsValidator(IOptions<ClusterOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);
            this.options = options.Value;
        }

        /// <inheritdoc />
        public void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(this.options.ClusterId))
            {
                throw new OrleansConfigurationException(
                    $"Configuration for {nameof(ClusterOptions)} is invalid. " +
                    $"A non-empty value for {nameof(options.ClusterId)} is required. " +
                    $"See {Constants.TroubleshootingHelpLink} for more information.");
            }

            if (string.IsNullOrWhiteSpace(this.options.ServiceId))
            {
                throw new OrleansConfigurationException(
                    $"Configuration for {nameof(ClusterOptions)} is invalid. " +
                    $"A non-empty value for {nameof(options.ServiceId)} is required. " +
                    $"See {Constants.TroubleshootingHelpLink} for more information.");
            }
        }
    }
}
