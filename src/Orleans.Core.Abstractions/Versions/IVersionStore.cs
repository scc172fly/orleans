using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Versions.Compatibility;
using Orleans.Versions.Selector;

namespace Orleans.Versions
{
    /// <summary>
    /// 进入可调整的实时 grain 接口版本战略功能。
    /// </summary>
    public interface IVersionStore : IVersionManager
    {
        /// <summary>
        /// 获得一个值,以显示是否启用此实例。
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// 将映射从 grain 接口类型获取到 grain 接口版本兼容性战略。
        /// </summary>
        /// <returns>地图图 grain 界面类型 grain 接口版本兼容战略。</returns>
        [Alias("GetCompatibilityStrategies")]
        Task<Dictionary<GrainInterfaceType, CompatibilityStrategy>> GetCompatibilityStrategies();

        /// <summary>获取配置的兼容策略。</summary>
        [Alias("245EF151")]
        Task<Dictionary<GrainInterfaceType, CompatibilityStrategy>> GetCompatibilityStrategies(CancellationToken cancellationToken)
            => GetCompatibilityStrategies();

        /// <summary>
        /// 将映射从 grain 接口类型获取到 grain 接口版本选择器战略。
        /// </summary>
        /// <returns>地图图 grain 界面类型 grain 界面版本选择器战略。</returns>
        [Alias("GetSelectorStrategies")]
        Task<Dictionary<GrainInterfaceType, VersionSelectorStrategy>> GetSelectorStrategies();

        /// <summary>获取配置的选取策略。</summary>
        [Alias("CE5EE42F")]
        Task<Dictionary<GrainInterfaceType, VersionSelectorStrategy>> GetSelectorStrategies(CancellationToken cancellationToken)
            => GetSelectorStrategies();

        /// <summary>
        /// 获取默认 grain 接口版本兼容性战略。
        /// </summary>
        /// <returns>默认默认 grain 接口版本兼容战略。</returns>
        [Alias("GetCompatibilityStrategy")]
        Task<CompatibilityStrategy?> GetCompatibilityStrategy();

        /// <summary>获得默认兼容策略。</summary>
        [Alias("294392A7")]
        Task<CompatibilityStrategy?> GetCompatibilityStrategy(CancellationToken cancellationToken)
            => GetCompatibilityStrategy();

        /// <summary>
        /// 获取默认 grain 接口版本选择器战略。
        /// </summary>
        /// <returns>默认 grain 接口版本选择器战略。</returns>
        [Alias("GetSelectorStrategy")]
        Task<VersionSelectorStrategy?> GetSelectorStrategy();

        /// <summary>获得默认选择器策略。</summary>
        [Alias("E9C4A2A7")]
        Task<VersionSelectorStrategy?> GetSelectorStrategy(CancellationToken cancellationToken)
            => GetSelectorStrategy();
    }
}
