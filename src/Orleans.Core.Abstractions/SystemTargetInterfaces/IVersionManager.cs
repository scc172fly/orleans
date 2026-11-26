using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Versions.Compatibility;
using Orleans.Versions.Selector;

namespace Orleans
{
    /// <summary>
    /// 管理如何谈判 grain 接口版本的职能。
    /// </summary>
    public interface IVersionManager
    {
        /// <summary>
        /// 设定兼容性策略
        /// </summary>
        /// <param name="strategy">要设置的战略。 设为 <see langword="null"/> , 以恢复配置中提供的默认战略。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("SetCompatibilityStrategy")]
        Task SetCompatibilityStrategy(CompatibilityStrategy strategy);

        /// <summary>设定兼容性策略</summary>
        [Alias("8F5C15A9")]
        Task SetCompatibilityStrategy(CompatibilityStrategy strategy, CancellationToken cancellationToken)
            => SetCompatibilityStrategy(strategy);

        /// <summary>
        /// 设置选择方策略。
        /// </summary>
        /// <param name="strategy">要设置的战略。 设为 <see langword="null"/> , 以恢复配置中提供的默认战略。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("SetSelectorStrategy")]
        Task SetSelectorStrategy(VersionSelectorStrategy strategy);

        /// <summary>设置选择器战略。</summary>
        [Alias("4AAEAFCE")]
        Task SetSelectorStrategy(VersionSelectorStrategy strategy, CancellationToken cancellationToken)
            => SetSelectorStrategy(strategy);

        /// <summary>
        /// 设定特定界面的兼容策略。
        /// </summary>
        /// <param name="interfaceType">接口的类型。</param>
        /// <param name="strategy">要设置的战略。 设为 <see langword="null"/> , 以恢复配置中提供的默认战略。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("SetCompatibilityStrategyForInterface")]
        Task SetCompatibilityStrategy(GrainInterfaceType interfaceType, CompatibilityStrategy strategy);

        /// <summary>设置特定接口的兼容战略。</summary>
        [Alias("C01C4EE8")]
        Task SetCompatibilityStrategy(
            GrainInterfaceType interfaceType,
            CompatibilityStrategy strategy,
            CancellationToken cancellationToken)
            => SetCompatibilityStrategy(interfaceType, strategy);

        /// <summary>
        /// 设置特定界面的选择方策略。
        /// </summary>
        /// <param name="interfaceType">接口的类型。</param>
        /// <param name="strategy">要设置的战略。 设为 <see langword="null"/> , 以恢复配置中提供的默认战略。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("SetSelectorStrategyForInterface")]
        Task SetSelectorStrategy(GrainInterfaceType interfaceType, VersionSelectorStrategy strategy);

        /// <summary>设置特定接口的选择器战略。</summary>
        [Alias("90AB9D5E")]
        Task SetSelectorStrategy(
            GrainInterfaceType interfaceType,
            VersionSelectorStrategy strategy,
            CancellationToken cancellationToken)
            => SetSelectorStrategy(interfaceType, strategy);
    }
}
