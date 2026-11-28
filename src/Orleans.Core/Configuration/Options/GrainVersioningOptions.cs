
using Orleans.Versions.Compatibility;
using Orleans.Versions.Selector;

namespace Orleans.Configuration
{
    /// <summary>
    /// 版本控制选项决定异构部署中如何选择 Grain 实现。
    /// </summary>
    public class GrainVersioningOptions
    {
        /// <summary>
        /// 获取或设置异构部署中用于确定 Grain 兼容性的默认策略名称。
        /// </summary>
        /// <value>默认使用 <see cref="BackwardCompatible"/> 策略。</value>
        public string DefaultCompatibilityStrategy { get; set; } = nameof(BackwardCompatible);

        /// <summary>
        /// 获取或设置异构部署中用于选择 Grain 版本的默认策略名称。
        /// </summary>
        /// <value>默认使用 <see cref="AllCompatibleVersions"/> 策略。</value>
        public string DefaultVersionSelectorStrategy { get; set; } = nameof(AllCompatibleVersions);
    }
}
