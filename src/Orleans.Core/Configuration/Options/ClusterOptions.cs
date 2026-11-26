using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 Orleans 组。
    /// </summary>
    public class ClusterOptions
    {
        /// <summary>
        /// 默认值 <see cref="ClusterId"/>。
        /// </summary>
        public const string DefaultClusterId = "default";

        /// <summary>
        /// 默认值 <see cref="ServiceId"/>。
        /// </summary>
        public const string DefaultServiceId = "default";

        /// <summary>
        /// 开发组的默认群集 ID。
        /// </summary>
        internal const string DevelopmentClusterId = "dev";

        /// <summary>
        /// 开发组群的默认服务 ID 。
        /// </summary>
        internal const string DevelopmentServiceId = "dev";

        /// <summary>
        /// 获取或设置集集身份。这曾经被称为 DeploymentId 之前 Orleans 2.0 名。
        /// </summary>
        public string ClusterId { get; set; } = DefaultClusterId;

        /// <summary>
        /// 获取或设置此项服务的独特标识符,在 <see cref="ClusterId"/> 可能无法提供的情况下,该标识符应能在部署和重新部署后幸存。
        /// </summary>
        public string ServiceId { get; set; } = DefaultServiceId;
    }

    /// <summary>
    /// <see cref="ClusterOptions"/>的校验符
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
