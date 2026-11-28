using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Versions.Compatibility;
using Orleans.Versions.Selector;

namespace Orleans.Versions
{
    /// <summary>
    /// 提供访问可在运行时修改的 Grain 接口版本策略的功能。
    /// </summary>
    public interface IVersionStore : IVersionManager
    {
        /// <summary>
        /// 获取指示此实例是否已启用的值。
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// 获取 Grain 接口类型到 Grain 接口版本兼容性策略的映射。
        /// </summary>
        /// <returns>Grain 接口类型到 Grain 接口版本兼容性策略的映射。</returns>
        [Alias("GetCompatibilityStrategies")]
        Task<Dictionary<GrainInterfaceType, CompatibilityStrategy>> GetCompatibilityStrategies();

        /// <summary>获取已配置的兼容性策略。</summary>
        [Alias("245EF151")]
        Task<Dictionary<GrainInterfaceType, CompatibilityStrategy>> GetCompatibilityStrategies(CancellationToken cancellationToken)
            => GetCompatibilityStrategies();

        /// <summary>
        /// 获取 Grain 接口类型到 Grain 接口版本选择策略的映射。
        /// </summary>
        /// <returns>Grain 接口类型到 Grain 接口版本选择策略的映射。</returns>
        [Alias("GetSelectorStrategies")]
        Task<Dictionary<GrainInterfaceType, VersionSelectorStrategy>> GetSelectorStrategies();

        /// <summary>获取已配置的选择策略。</summary>
        [Alias("CE5EE42F")]
        Task<Dictionary<GrainInterfaceType, VersionSelectorStrategy>> GetSelectorStrategies(CancellationToken cancellationToken)
            => GetSelectorStrategies();

        /// <summary>
        /// 获取默认 Grain 接口版本兼容性策略。
        /// </summary>
        /// <returns>默认 Grain 接口版本兼容性策略。</returns>
        [Alias("GetCompatibilityStrategy")]
        Task<CompatibilityStrategy?> GetCompatibilityStrategy();

        /// <summary>获取默认兼容性策略。</summary>
        [Alias("294392A7")]
        Task<CompatibilityStrategy?> GetCompatibilityStrategy(CancellationToken cancellationToken)
            => GetCompatibilityStrategy();

        /// <summary>
        /// 获取默认 Grain 接口版本选择策略。
        /// </summary>
        /// <returns>默认 Grain 接口版本选择策略。</returns>
        [Alias("GetSelectorStrategy")]
        Task<VersionSelectorStrategy?> GetSelectorStrategy();

        /// <summary>获取默认选择策略。</summary>
        [Alias("E9C4A2A7")]
        Task<VersionSelectorStrategy?> GetSelectorStrategy(CancellationToken cancellationToken)
            => GetSelectorStrategy();
    }
}
