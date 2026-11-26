using System;

namespace Orleans.Transactions.TestKit.Consistency
{
    /// <summary>
    /// 配置由一致性测试 grains产生的随机交易。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class ConsistencyTestOptions
    {
        /// <summary>
        /// 获取或设置用于生成可重复测试操作的种子。
        /// </summary>
        [Id(0)]
        public int RandomSeed { get; set; } = 0;

        /// <summary>
        /// 获取或设置参加测试的 grains 数量。
        /// </summary>
        [Id(1)]
        public int NumGrains { get; set; } = 50;

        /// <summary>
        /// 获取或设置最大嵌套 grain 调用深度。
        /// </summary>
        [Id(2)]
        public int MaxDepth { get; set; } = 5;

        /// <summary>
        /// 获取或设定一个值,显示是否为避免僵局而命令嵌套 grain 电话。
        /// </summary>
        [Id(3)]
        public bool AvoidDeadlocks { get; set; } = true;

        /// <summary>
        /// 获取或设定一个值,以显示嵌套呼叫调度是否在交易超时前停止。
        /// </summary>
        [Id(4)]
        public bool AvoidTimeouts { get; set; } = true;

        /// <summary>
        /// 获取或设定测试如何决定每次操作是否读写状态 。
        /// </summary>
        [Id(5)]
        public ReadWriteDetermination ReadWrite { get; set; } = ReadWriteDetermination.PerGrain;

        /// <summary>
        /// 获取或设置基底 grain 键添加到每个逻辑 grain 数字。
        /// </summary>
        [Id(6)]
        public long GrainOffset { get; set; }

        /// <summary>
        /// 一致性测试中 grains 号数的独家上限。
        /// </summary>
        public const int MaxGrains = 100000;
    }

    /// <summary>
    /// 指定随机一致性测试选择读写操作的范围。
    /// </summary>
    public enum ReadWriteDetermination
    {
        /// <summary>
        /// 选择交易中所有存取的操作类型。
        /// </summary>
        PerTransaction,

        /// <summary>
        /// 为交易访问的每一 grain 选择一种操作类型。
        /// </summary>
        PerGrain,

        /// <summary>
        /// 为每个 grain 访问选择独立的操作类型。
        /// </summary>
        PerAccess
    }
}
