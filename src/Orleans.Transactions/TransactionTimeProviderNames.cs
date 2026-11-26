namespace Orleans.Transactions;

/// <summary>
/// 用于解决按键依赖性交易子系统 <see cref="System.TimeProvider"/> 的事务关键
/// 。请参见核心运行时间区域 <see cref="Orleans.Runtime.TimeProviderNames"/> 。
/// </summary>
public static class TransactionTimeProviderNames
{
    /// <summary>
    /// 交易子系统所用的时钟。
    /// </summary>
    public const string Transactions = "Orleans.Transactions";
}
