using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Runtime.MembershipService;

namespace Orleans.Configuration
{
    /// <summary>
    /// 验证 <see cref="AdoNetClusteringClientOptions"/> 配置。
    /// </summary>
    public class AdoNetClusteringClientOptionsValidator : IConfigurationValidator
    {
        private readonly AdoNetClusteringClientOptions options;

        /// <summary>
        /// 初始化 <see cref="AdoNetClusteringClientOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">要验证的选项。</param>
        public AdoNetClusteringClientOptionsValidator(IOptions<AdoNetClusteringClientOptions> options)
        {
            this.options = options.Value;
        }

        /// <inheritdoc />
        public void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(this.options.Invariant))
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetClusteringClientOptions)} values for {nameof(AdoNetClusteringTable)}. {nameof(options.Invariant)} is required.");
            }

            if (string.IsNullOrWhiteSpace(this.options.ConnectionString) == (this.options.DataSource is null))
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetClusteringClientOptions)} values for {nameof(AdoNetClusteringTable)}. Configure exactly one of {nameof(options.ConnectionString)} or {nameof(options.DataSource)}.");
            }
        }
    }
}
