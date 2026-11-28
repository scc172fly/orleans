using Orleans.Serialization;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 使用 Orleans 序列化系统复制事务数据。
    /// </summary>
    /// <typeparam name="TData">事务数据类型。</typeparam>
    public class DefaultTransactionDataCopier<TData> : ITransactionDataCopier<TData>
    {
        private readonly DeepCopier<TData> deepCopier;

        /// <summary>
        /// 初始化 <see cref="DefaultTransactionDataCopier{TData}"/> 类的新实例。
        /// </summary>
        /// <param name="deepCopier">用于创建事务数据独立副本的复制器。</param>
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
