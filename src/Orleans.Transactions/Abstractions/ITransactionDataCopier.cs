
namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 创建交易所使用的数据的独立副本。
    /// </summary>
    /// <typeparam name="TData">数据类型。</typeparam>
    public interface ITransactionDataCopier<TData>
    {
        /// <summary>
        /// 创建指定值的深重副本。
        /// </summary>
        /// <param name="original">要复制的值。</param>
        /// <returns>独立副本 <paramref name="original"/>.</returns>
        TData DeepCopy(TData original);
    }
}
