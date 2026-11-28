namespace Orleans.Transactions;

/// <summary>
/// 用于通过键控依赖注入解析事务子系统 <see cref="System.TimeProvider"/> 的服务键。
/// 核心运行时各区域使用的服务键参见 <see cref="Orleans.Runtime.TimeProviderNames"/>。
/// </summary>
public static class TransactionTimeProviderNames
{
    /// <summary>
    /// 事务子系统使用的时钟。
    /// </summary>
    public const string Transactions = "Orleans.Transactions";
}
