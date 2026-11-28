using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Versions.Compatibility;
using Orleans.Versions.Selector;

namespace Orleans
{
    /// <summary>
    /// 提供管理 Grain 接口版本协商方式的功能。
    /// </summary>
    public interface IVersionManager
    {
        /// <summary>
        /// 设置兼容性策略。
        /// </summary>
        /// <param name="strategy">要设置的策略。设为 <see langword="null"/> 可恢复为配置中提供的默认策略。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("SetCompatibilityStrategy")]
        Task SetCompatibilityStrategy(CompatibilityStrategy strategy);

        /// <summary>设置兼容性策略。</summary>
        [Alias("8F5C15A9")]
        Task SetCompatibilityStrategy(CompatibilityStrategy strategy, CancellationToken cancellationToken)
            => SetCompatibilityStrategy(strategy);

        /// <summary>
        /// 设置选择策略。
        /// </summary>
        /// <param name="strategy">要设置的策略。设为 <see langword="null"/> 可恢复为配置中提供的默认策略。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("SetSelectorStrategy")]
        Task SetSelectorStrategy(VersionSelectorStrategy strategy);

        /// <summary>设置选择策略。</summary>
        [Alias("4AAEAFCE")]
        Task SetSelectorStrategy(VersionSelectorStrategy strategy, CancellationToken cancellationToken)
            => SetSelectorStrategy(strategy);

        /// <summary>
        /// 为指定接口设置兼容性策略。
        /// </summary>
        /// <param name="interfaceType">接口的类型。</param>
        /// <param name="strategy">要设置的策略。设为 <see langword="null"/> 可恢复为配置中提供的默认策略。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("SetCompatibilityStrategyForInterface")]
        Task SetCompatibilityStrategy(GrainInterfaceType interfaceType, CompatibilityStrategy strategy);

        /// <summary>为指定接口设置兼容性策略。</summary>
        [Alias("C01C4EE8")]
        Task SetCompatibilityStrategy(
            GrainInterfaceType interfaceType,
            CompatibilityStrategy strategy,
            CancellationToken cancellationToken)
            => SetCompatibilityStrategy(interfaceType, strategy);

        /// <summary>
        /// 为指定接口设置选择策略。
        /// </summary>
        /// <param name="interfaceType">接口的类型。</param>
        /// <param name="strategy">要设置的策略。设为 <see langword="null"/> 可恢复为配置中提供的默认策略。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("SetSelectorStrategyForInterface")]
        Task SetSelectorStrategy(GrainInterfaceType interfaceType, VersionSelectorStrategy strategy);

        /// <summary>为指定接口设置选择策略。</summary>
        [Alias("90AB9D5E")]
        Task SetSelectorStrategy(
            GrainInterfaceType interfaceType,
            VersionSelectorStrategy strategy,
            CancellationToken cancellationToken)
            => SetSelectorStrategy(interfaceType, strategy);
    }
}
