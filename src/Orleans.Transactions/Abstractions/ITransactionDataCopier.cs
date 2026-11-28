
namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 为事务使用的数据创建独立副本。
    /// </summary>
    /// <typeparam name="TData">数据类型。</typeparam>
    public interface ITransactionDataCopier<TData>
    {
        /// <summary>
        /// 创建指定值的深拷贝。
        /// </summary>
        /// <param name="original">要复制的值。</param>
        /// <returns><paramref name="original"/> 的独立副本。</returns>
        TData DeepCopy(TData original);
    }
}
