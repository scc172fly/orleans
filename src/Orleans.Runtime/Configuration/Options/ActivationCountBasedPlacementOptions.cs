using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Configuration
{
    /// <summary>
    /// 使用 <see cref="ActivationCountBasedPlacement"/> 时调节集群内 Grain 放置的设置。
    /// </summary>
    public class ActivationCountBasedPlacementOptions
    {
        /// <summary>
        /// 获取或设置使用激活实例计数放置策略时随机选取以供考虑的 Silo 数量。
        /// 仅用于激活实例计数放置策略。
        /// </summary>
        public int ChooseOutOf { get; set; } = DEFAULT_ACTIVATION_COUNT_PLACEMENT_CHOOSE_OUT_OF;

        /// <summary>
        /// 作出放置决策时可供选择的 Silo 默认数量。
        /// </summary>
        public const int DEFAULT_ACTIVATION_COUNT_PLACEMENT_CHOOSE_OUT_OF = 2;
    }

    /// <summary>
    /// 验证 <see cref="ActivationCountBasedPlacementOptions"/> 的属性。
    /// </summary>
    internal class ActivationCountBasedPlacementOptionsValidator : IConfigurationValidator
    {
        private readonly ActivationCountBasedPlacementOptions options;

        public ActivationCountBasedPlacementOptionsValidator(IOptions<ActivationCountBasedPlacementOptions> options)
        {
            this.options = options.Value;
        }

        /// <inheritdoc />
        public void ValidateConfiguration()
        {
            if (this.options.ChooseOutOf <= 0)
            {
                throw new OrleansConfigurationException(
                    $"The value of {nameof(ActivationCountBasedPlacementOptions)}.{nameof(this.options.ChooseOutOf)} must be greater than 0.");
            }
        }
    }
}
