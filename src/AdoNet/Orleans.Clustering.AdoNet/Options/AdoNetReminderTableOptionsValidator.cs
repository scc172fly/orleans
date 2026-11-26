using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Runtime.MembershipService;

namespace Orleans.Configuration
{
    /// <summary>
    /// 校验 <see cref="AdoNetClusteringSiloOptions"/> 配置。
    /// </summary>
    public class AdoNetClusteringSiloOptionsValidator : IConfigurationValidator
    {
        private readonly AdoNetClusteringSiloOptions options;

        /// <summary>
        /// 初始化 <see cref="AdoNetClusteringSiloOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">验证的选项。</param>
        public AdoNetClusteringSiloOptionsValidator(IOptions<AdoNetClusteringSiloOptions> options)
        {
            this.options = options.Value;
        }

        /// <inheritdoc />
        public void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(this.options.Invariant))
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetClusteringSiloOptions)} values for {nameof(AdoNetClusteringTable)}. {nameof(options.Invariant)} is required.");
            }

            if (string.IsNullOrWhiteSpace(this.options.ConnectionString) == (this.options.DataSource is null))
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetClusteringSiloOptions)} values for {nameof(AdoNetClusteringTable)}. Configure exactly one of {nameof(options.ConnectionString)} or {nameof(options.DataSource)}.");
            }
        }
    }
}
