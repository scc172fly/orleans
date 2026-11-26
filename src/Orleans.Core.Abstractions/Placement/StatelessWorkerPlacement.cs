using System;
using System.Collections.Generic;
using System.Globalization;
using Orleans.Metadata;

namespace Orleans.Runtime
{
    /// <summary>
    /// 无国籍工人安置战略允许多个特定 grain 在任何东道方同时并存,并保留给无国籍工人 grains。
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
        /// 获取给定 grain同时活动的本地例的最大数量。
        /// </summary>
        [Id(0)]
        public int MaxLocal { get; private set; }

        /// <summary>
        /// 当设定为 <see langword="true"/>时,闲置工人将在运行时间前主动停止工作。
        /// 否则,如果 <see langword="false"/>,工人比他们更会根据收税年龄被解除职务。
        /// </summary>
        [Id(1)]
        public bool RemoveIdleWorkers { get; private set; } = true;

        /// <summary>
        /// 初始化 <see cref="StatelessWorkerPlacement"/> 类的新实例。
        /// </summary>
        /// <param name="maxLocal">
        /// 给定 grain可同时活动的本地例数的最大数量。
        /// </param>
        internal StatelessWorkerPlacement(int maxLocal) : this(maxLocal, true)
        {
        }

        /// <summary>
        /// 初始化 <see cref="StatelessWorkerPlacement"/> 类的新实例。
        /// </summary>
        /// <param name="maxLocal">
        /// 给定 grain可同时活动的本地例数的最大数量。
        /// </param>
        /// <param name="removeIdleWorkers">
        /// 闲置工人是否在运行时主动停止工作,而不是仅仅根据收集年龄停止工作。
        /// </param>
        internal StatelessWorkerPlacement(int maxLocal, bool removeIdleWorkers)
        {
            // 如果 maxLocal 未在 StatelessWorkerAttribute,
            // 我们将使用 defaultMaxStatelessWorkers,即 System.Environment.ProcessorCount。
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
