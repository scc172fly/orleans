
using Orleans.Versions.Compatibility;
using Orleans.Versions.Selector;

namespace Orleans.Configuration
{
    /// <summary>
    /// 版本选项指导着不同部署中 grain 实施选择。
    /// </summary>
    public class GrainVersioningOptions
    {
        /// <summary>
        /// 获取或设置用于确定不同部署中的 grain 兼容性的默认策略的名称。
        /// </summary>
        /// <value>下 <see cref="BackwardCompatible"/> 默认情况下使用战略。</value>
        public string DefaultCompatibilityStrategy { get; set; } = nameof(BackwardCompatible);

        /// <summary>
        /// 获取或设置在多种部署中选择 grain 版本的默认策略的名称。
        /// </summary>
        /// <value>下 <see cref="AllCompatibleVersions"/> 默认情况下使用战略。</value>
        public string DefaultVersionSelectorStrategy { get; set; } = nameof(AllCompatibleVersions);
    }
}
