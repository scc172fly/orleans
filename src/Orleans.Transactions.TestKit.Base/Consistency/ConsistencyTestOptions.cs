using System;

namespace Orleans.Transactions.TestKit.Consistency
{
    /// <summary>
    /// 配置一致性测试 Grain 生成的随机事务。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class ConsistencyTestOptions
    {
        /// <summary>
        /// 获取或设置用于生成可复现测试操作的随机种子。
        /// </summary>
        [Id(0)]
        public int RandomSeed { get; set; } = 0;

        /// <summary>
        /// 获取或设置参与测试的 Grain 数量。
        /// </summary>
        [Id(1)]
        public int NumGrains { get; set; } = 50;

        /// <summary>
        /// 获取或设置嵌套 Grain 调用的最大深度。
        /// </summary>
        [Id(2)]
        public int MaxDepth { get; set; } = 5;

        /// <summary>
        /// 获取或设置一个值，指示是否对嵌套 Grain 调用排序以避免死锁。
        /// </summary>
        [Id(3)]
        public bool AvoidDeadlocks { get; set; } = true;

        /// <summary>
        /// 获取或设置一个值，指示是否在事务超时前停止调度嵌套调用。
        /// </summary>
        [Id(4)]
        public bool AvoidTimeouts { get; set; } = true;

        /// <summary>
        /// 获取或设置测试确定每次操作是读取还是写入状态的方式。
        /// </summary>
        [Id(5)]
        public ReadWriteDetermination ReadWrite { get; set; } = ReadWriteDetermination.PerGrain;

        /// <summary>
        /// 获取或设置与每个逻辑 Grain 编号相加的基础 Grain 键。
        /// </summary>
        [Id(6)]
        public long GrainOffset { get; set; }

        /// <summary>
        /// 一致性测试中 Grain 数量的上界（不含该值）。
        /// </summary>
        public const int MaxGrains = 100000;
    }

    /// <summary>
    /// 指定随机一致性测试选择读操作或写操作的作用范围。
    /// </summary>
    public enum ReadWriteDetermination
    {
        /// <summary>
        /// 为事务中的所有访问选择同一种操作。
        /// </summary>
        PerTransaction,

        /// <summary>
        /// 为事务访问的每个 Grain 选择一种操作。
        /// </summary>
        PerGrain,

        /// <summary>
        /// 对每次 Grain 访问独立选择操作类型。
        /// </summary>
        PerAccess
    }
}
