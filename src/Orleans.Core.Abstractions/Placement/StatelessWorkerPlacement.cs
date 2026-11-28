using System;
using System.Collections.Generic;
using System.Globalization;
using Orleans.Metadata;

namespace Orleans.Runtime
{
    /// <summary>
    /// 无状态工作者放置策略允许给定 Grain 的多个实例在任何主机上同时共存，且专用于无状态工作者 Grain。
    /// </summary>
    [Serializable, GenerateSerializer]
    internal sealed class StatelessWorkerPlacement : PlacementStrategy
    {
        private const string MaxLocalPropertyKey = "max-local-instances";
        private const string RemoveIdleWorkersPropertyKey = "remove-idle-workers";

        private static readonly int DefaultMaxStatelessWorkers = Environment.ProcessorCount;

        /// <inheritdoc/>
        public override bool IsUsingGrainDirectory => false;

        /// <summary>
        /// 获取给定 Grain 在本地可以同时处于活动状态的实例的最大数量。
        /// </summary>
        [Id(0)]
        public int MaxLocal { get; private set; }

        /// <summary>
        /// 当设置为 <see langword="true"/> 时，运行时将主动停用空闲工作者。
        /// 否则，当设置为 <see langword="false"/> 时，将根据空闲回收阈值停用工作者。
        /// </summary>
        [Id(1)]
        public bool RemoveIdleWorkers { get; private set; } = true;

        /// <summary>
        /// 初始化 <see cref="StatelessWorkerPlacement"/> 类的新实例。
        /// </summary>
        /// <param name="maxLocal">
        /// 给定 Grain 在本地可以同时处于活动状态的实例的最大数量。
        /// </param>
        internal StatelessWorkerPlacement(int maxLocal) : this(maxLocal, true)
        {
        }

        /// <summary>
        /// 初始化 <see cref="StatelessWorkerPlacement"/> 类的新实例。
        /// </summary>
        /// <param name="maxLocal">
        /// 给定 Grain 在本地可以同时处于活动状态的实例的最大数量。
        /// </param>
        /// <param name="removeIdleWorkers">
        /// 是否由运行时主动停用空闲工作者，而不仅依据空闲回收阈值来停用。
        /// </param>
        internal StatelessWorkerPlacement(int maxLocal, bool removeIdleWorkers)
        {
            // 如果未在 StatelessWorkerAttribute 上指定 maxLocal，
            // 则使用 defaultMaxStatelessWorkers，即 System.Environment.ProcessorCount。
            this.MaxLocal = maxLocal > 0 ? maxLocal : DefaultMaxStatelessWorkers;
            this.RemoveIdleWorkers = removeIdleWorkers;
        }

        /// <summary>
        /// 初始化 <see cref="StatelessWorkerPlacement"/> 类的新实例。
        /// </summary>
        public StatelessWorkerPlacement() : this(-1)
        {
        }

        /// <inheritdoc/>
        public override string ToString() => $"StatelessWorkerPlacement(MaxLocal={MaxLocal}, RemoveIdleWorkers={RemoveIdleWorkers})";

        /// <inheritdoc/>
        public override void Initialize(GrainProperties properties)
        {
            base.Initialize(properties);

            if (properties.Properties.TryGetValue(MaxLocalPropertyKey, out var maxLocalValue) &&
                !string.IsNullOrWhiteSpace(maxLocalValue))
            {
                if (int.TryParse(maxLocalValue, out var maxLocal))
                {
                    MaxLocal = maxLocal;
                }
            }

            if (properties.Properties.TryGetValue(RemoveIdleWorkersPropertyKey, out var removeIdleValue) &&
                !string.IsNullOrWhiteSpace(removeIdleValue))
            {
                if (bool.TryParse(removeIdleValue, out var removeIdle))
                {
                    RemoveIdleWorkers = removeIdle;
                }
            }
        }

        /// <inheritdoc/>
        public override void PopulateGrainProperties(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        {
            properties[MaxLocalPropertyKey] = MaxLocal.ToString(CultureInfo.InvariantCulture);
            properties[RemoveIdleWorkersPropertyKey] = RemoveIdleWorkers.ToString(CultureInfo.InvariantCulture);

            base.PopulateGrainProperties(services, grainClass, grainType, properties);
        }
    }
}
