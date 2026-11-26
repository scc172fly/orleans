using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Configuration
{
    /// <summary>
    /// 管理设置设置的设置 grains 使用时横横集 <see cref="ActivationCountBasedPlacement"/>.
    /// </summary>
    public class ActivationCountBasedPlacementOptions
    {
        /// <summary>
        /// 使用激活计数定位策略时, 获取或设置随机选择的 silos 数字。
        /// 仅用于激活计数安置政策。
        /// </summary>
        public int ChooseOutOf { get; set; } = DEFAULT_ACTIVATION_COUNT_PLACEMENT_CHOOSE_OUT_OF;

        /// <summary>
        /// 在做出安置决定时选择的默认 silos 数。
        /// </summary>
        public const int DEFAULT_ACTIVATION_COUNT_PLACEMENT_CHOOSE_OUT_OF = 2;
    }

    /// <summary>
    /// 校验 <see cref="ActivationCountBasedPlacementOptions"/> 属性。
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
