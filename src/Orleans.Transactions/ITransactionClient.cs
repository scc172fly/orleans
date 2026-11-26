using System;
using System.Threading.Tasks;

namespace Orleans;

/// <summary>
/// 使用 Orleans 交易语义执行代表。
/// </summary>
public interface ITransactionClient
{
    /// <summary>
    /// 使用指定的交易选项执行代表。 新创建的交易在代表成功完成后进行; 合并的交易记录该参与者的承付票 。
    /// </summary>
    /// <param name="transactionOption">交易的创造、加入或抑制行为</param>
    /// <param name="transactionDelegate">执行代表。</param>
    /// <returns>A <see cref="Task"/> 代表交易业务。</returns>
    Task RunTransaction(TransactionOption transactionOption, Func<Task> transactionDelegate);

    /// <summary>
    /// 使用指定的交易选项执行代表。
    /// </summary>
    /// <param name="transactionOption">交易的创造、加入或抑制行为</param>
    /// <param name="transactionDelegate">
    /// 执行代表。 其结果显示交易是否应该进行。
    /// </param>
    /// <returns>A <see cref="Task"/> 代表交易业务。</returns>
    Task RunTransaction(TransactionOption transactionOption, Func<Task<bool>> transactionDelegate);

    /// <summary>
    /// 使用指定的交易选项执行代表。 新创建的交易在代表成功完成后进行; 合并的交易记录该参与者的承付票 。
    /// </summary>
    /// <param name="transactionOption">交易的创造、加入或抑制行为</param>
    /// <param name="transactionDelegate">执行代表。</param>
    /// <param name="useExclusiveLock"> <see langword="true"/>时,交易期间访问的所有交易状态
    /// 即使在读操作中,将获得独家锁,防止在争议激烈的情况下发生锁定升级冲突。</param>
    /// <returns>A <see cref="Task"/> 代表交易业务。</returns>
    Task RunTransaction(TransactionOption transactionOption, Func<Task> transactionDelegate, bool useExclusiveLock);

    /// <summary>
    /// 使用指定的交易选项执行代表。
    /// </summary>
    /// <param name="transactionOption">交易的创造、加入或抑制行为</param>
    /// <param name="transactionDelegate">
    /// 执行代表。 其结果显示交易是否应该进行。
    /// </param>
    /// <param name="useExclusiveLock"> <see langword="true"/>时,交易期间访问的所有交易状态
    /// 即使在读操作中,将获得独家锁,防止在争议激烈的情况下发生锁定升级冲突。</param>
    /// <returns>A <see cref="Task"/> 代表交易业务。</returns>
    Task RunTransaction(TransactionOption transactionOption, Func<Task<bool>> transactionDelegate, bool useExclusiveLock);
}
