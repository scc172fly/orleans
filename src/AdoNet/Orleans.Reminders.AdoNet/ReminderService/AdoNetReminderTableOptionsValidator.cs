using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Runtime.ReminderService;

namespace Orleans.Configuration
{
    /// <summary>
    /// 校验 <see cref="AdoNetReminderTableOptions"/> 配置。
    /// </summary>
    public class AdoNetReminderTableOptionsValidator : IConfigurationValidator
    {
        private readonly AdoNetReminderTableOptions options;

        /// <summary>
        /// 初始化 <see cref="AdoNetReminderTableOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">提醒表选项进行验证。</param>
        public AdoNetReminderTableOptionsValidator(IOptions<AdoNetReminderTableOptions> options)
        {
            this.options = options.Value;
        }

        /// <inheritdoc />
        public void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(this.options.Invariant))
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetReminderTableOptions)} values for {nameof(AdoNetReminderTable)}. {nameof(options.Invariant)} is required.");
            }

            if (string.IsNullOrWhiteSpace(this.options.ConnectionString) == (this.options.DataSource is null))
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetReminderTableOptions)} values for {nameof(AdoNetReminderTable)}. Configure exactly one of {nameof(options.ConnectionString)} or {nameof(options.DataSource)}.");
            }
        }
    }
}
