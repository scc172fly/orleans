using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 与装载停止相关的选项。
    /// </summary>
    public class LoadSheddingOptions
    {
        /// <summary>
        /// <see cref="LoadSheddingLimit"/>的默认值。
        /// </summary>
        internal const int DefaultCpuThreshold = 95;

        /// <summary>
        /// <see cref="MemoryThreshold"/>的默认值。
        /// </summary>
        internal const int DefaultMemoryThreshold = 90;

        /// <summary>
        /// 获取或设定一个值,显示客户端和流源提供者是否启用了卸载。
        /// 默认值为 <see langword="false"/>,这意味着已禁用装载叠叠。
        /// </summary>
        /// <value>装入的切换因默认而禁用。</value>
        public bool LoadSheddingEnabled { get; set; }

        /// <summary>
        /// 获取或设置 CPU 利用率,以 <c>0</c> 和 <c>100</c>之间值表示,开始加载切换。
        /// 请注意,这个值为%,因此有效值介于1至100之间,合理值通常介于80至95之间。
        /// 如果加载延后被禁用, 则忽略此值, 默认是默认值 。
        /// </summary>
        /// <value>装入的切换从CPU默认使用95%的 95% 开始,如果启用了加载的切换。</value>
        /// <remarks>此属性已被折旧。 代之以使用 <see cref="CpuThreshold"/> 。</remarks>
        [Obsolete($"Use {nameof(CpuThreshold)} instead.", error: true)]
        public int LoadSheddingLimit { get => CpuThreshold; set => CpuThreshold = value; }

        /// <summary>
        /// 获取或设置 CPU 利用率,以 <c>0</c> 和 <c>100</c>之间值表示,开始加载切换。
        /// 请注意,这个值为%,因此有效值介于1至100之间,合理值通常介于80至95之间。
        /// 如果加载延后被禁用, 则忽略此值, 默认是默认值 。
        /// </summary>
        /// <value>装入的切换从CPU默认使用95%的 95% 开始,如果启用了加载的切换。</value>
        public int CpuThreshold { get; set; } = DefaultCpuThreshold;

        /// <summary>
        /// 获取或设置内存利用率,以 <c>0</c> 和 <c>100</c>之间开始加载切换的数值表示。
        /// 请注意,这个值为%,因此有效值介于1至100之间,合理值通常介于80至95之间。
        /// 如果加载延后被禁用, 则忽略此值, 默认是默认值 。
        /// </summary>
        /// <value>装入的切换从内存利用率90%默认时开始,如果启用了加载的切换。</value>
        public int MemoryThreshold { get; set; } = DefaultMemoryThreshold;
    }
}
