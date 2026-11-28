using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit.Consistency
{
    /// <summary>
    /// 定义用于生成一致性历史记录的随机事务操作。
    /// </summary>
    public interface IConsistencyTestGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 执行随机的事务读取、写入或嵌套 Grain 调用。
        /// </summary>
        /// <param name="options">一致性测试配置。</param>
        /// <param name="depth">当前嵌套调用深度。</param>
        /// <param name="stack">事务调用的诊断路径。</param>
        /// <param name="max">嵌套 Grain 选择范围的上界（不含该值）。</param>
        /// <param name="stopAfter">调度额外嵌套调用的截止时间。</param>
        /// <returns>事务观测到的状态版本。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<Observation[]> Run(ConsistencyTestOptions options, int depth, string stack, int max, DateTime stopAfter);
    }


    /// <summary>
    /// 表示用户有意发起的事务中止。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class UserAbort : Exception
    {
        /// <summary>
        /// 初始化 <see cref="UserAbort"/> 类的新实例。
        /// </summary>
        public UserAbort() : base("User aborted transaction") { }

        /// <summary>
        /// 从序列化数据初始化 <see cref="UserAbort"/> 类的新实例。
        /// </summary>
        /// <param name="info">已序列化的异常数据。</param>
        /// <param name="context">序列化操作的上下文。</param>
        [Obsolete]
        protected UserAbort(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

}
