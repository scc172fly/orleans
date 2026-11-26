using Orleans.Serialization;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 使用 Orleans 序列化系统复制交易数据。
    /// </summary>
    /// <typeparam name="TData">交易数据类型。</typeparam>
    public class DefaultTransactionDataCopier<TData> : ITransactionDataCopier<TData>
    {
        private readonly DeepCopier<TData> deepCopier;

        /// <summary>
        /// 初始化 <see cref="DefaultTransactionDataCopier{TData}"/> 类的新实例。
        /// </summary>
        /// <param name="deepCopier">用来创建独立交易数据副本的复印机。</param>
        public DefaultTransactionDataCopier(DeepCopier<TData> deepCopier)
        {
            this.deepCopier = deepCopier;
        }

        /// <inheritdoc/>
        public TData DeepCopy(TData original)
        {
            return (TData)this.deepCopier.Copy(original)!;
        }
    }
}
