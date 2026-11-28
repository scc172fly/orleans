using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 与负载削减有关的选项。
    /// </summary>
    public class LoadSheddingOptions
    {
        /// <summary>
        /// <see cref="LoadSheddingLimit"/> 的默认值。
        /// </summary>
        internal const int DefaultCpuThreshold = 95;

        /// <summary>
        /// <see cref="MemoryThreshold"/> 的默认值。
        /// </summary>
        internal const int DefaultMemoryThreshold = 90;

        /// <summary>
        /// 获取或设置指示是否启用客户端网关及流提供程序负载削减的值。
        /// 默认值为 <see langword="false"/>，表示禁用负载削减。
        /// </summary>
        /// <value>默认禁用负载削减。</value>
        public bool LoadSheddingEnabled { get; set; }

        /// <summary>
        /// 获取或设置开始负载削减时的 CPU 利用率，以 <c>0</c> 到 <c>100</c> 之间的值表示。
        /// 注意，此值的单位是百分比，因此有效范围为 1 到 100，通常合理的取值介于 80 和 95 之间。
        /// 如果禁用了负载削减（默认情况），则忽略此值。
        /// </summary>
        /// <value>启用负载削减时，默认在 CPU 利用率达到 95% 时开始削减负载。</value>
        /// <remarks>此属性已弃用，请改用 <see cref="CpuThreshold"/>。</remarks>
        [Obsolete($"Use {nameof(CpuThreshold)} instead.", error: true)]
        public int LoadSheddingLimit { get => CpuThreshold; set => CpuThreshold = value; }

        /// <summary>
        /// 获取或设置开始负载削减时的 CPU 利用率，以 <c>0</c> 到 <c>100</c> 之间的值表示。
        /// 注意，此值的单位是百分比，因此有效范围为 1 到 100，通常合理的取值介于 80 和 95 之间。
        /// 如果禁用了负载削减（默认情况），则忽略此值。
        /// </summary>
        /// <value>启用负载削减时，默认在 CPU 利用率达到 95% 时开始削减负载。</value>
        public int CpuThreshold { get; set; } = DefaultCpuThreshold;

        /// <summary>
        /// 获取或设置开始负载削减时的内存利用率，以 <c>0</c> 到 <c>100</c> 之间的值表示。
        /// 注意，此值的单位是百分比，因此有效范围为 1 到 100，通常合理的取值介于 80 和 95 之间。
        /// 如果禁用了负载削减（默认情况），则忽略此值。
        /// </summary>
        /// <value>启用负载削减时，默认在内存利用率达到 90% 时开始削减负载。</value>
        public int MemoryThreshold { get; set; } = DefaultMemoryThreshold;
    }
}
