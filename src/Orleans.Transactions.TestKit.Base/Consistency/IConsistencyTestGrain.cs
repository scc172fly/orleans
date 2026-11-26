using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit.Consistency
{
    /// <summary>
    /// 定义用于产生一致性历史的随机交易操作。
    /// </summary>
    public interface IConsistencyTestGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 执行随机化的交易读写或嵌套 grain 呼叫。
        /// </summary>
        /// <param name="options">一致性测试配置。</param>
        /// <param name="depth">当前嵌套的呼叫深度。</param>
        /// <param name="stack">交易呼叫的诊断路径。</param>
        /// <param name="max">嵌套 grain 选择的独家上吊框。</param>
        /// <param name="stopAfter">安排更多嵌套电话的截止日期。</param>
        /// <returns>交易所观察到的州版本。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<Observation[]> Run(ConsistencyTestOptions options, int depth, string stack, int max, DateTime stopAfter);
    }


    /// <summary>
    /// 表示用户故意发起的交易中止。
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
        /// 从序列数据初始化了 <see cref="UserAbort"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列的例外数据。</param>
        /// <param name="context">序列化操作的背景。</param>
        [Obsolete]
        protected UserAbort(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

}
