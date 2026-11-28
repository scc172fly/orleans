using System;
using System.Threading.Tasks;

namespace Orleans;

/// <summary>
/// 按照 Orleans 事务语义执行委托。
/// </summary>
public interface ITransactionClient
{
    /// <summary>
    /// 使用指定的事务选项执行委托。委托成功完成时，新创建的事务将提交；如果加入现有事务，则记录该参与者的提交投票。
    /// </summary>
    /// <param name="transactionOption">事务的创建、加入或抑制行为。</param>
    /// <param name="transactionDelegate">要执行的委托。</param>
    /// <returns>表示事务操作的 <see cref="Task"/>。</returns>
    Task RunTransaction(TransactionOption transactionOption, Func<Task> transactionDelegate);

    /// <summary>
    /// 使用指定的事务选项执行委托。
    /// </summary>
    /// <param name="transactionOption">事务的创建、加入或抑制行为。</param>
    /// <param name="transactionDelegate">
    /// 要执行的委托。其结果指示事务是否应提交。
    /// </param>
    /// <returns>表示事务操作的 <see cref="Task"/>。</returns>
    Task RunTransaction(TransactionOption transactionOption, Func<Task<bool>> transactionDelegate);

    /// <summary>
    /// 使用指定的事务选项执行委托。委托成功完成时，新创建的事务将提交；如果加入现有事务，则记录该参与者的提交投票。
    /// </summary>
    /// <param name="transactionOption">事务的创建、加入或抑制行为。</param>
    /// <param name="transactionDelegate">要执行的委托。</param>
    /// <param name="useExclusiveLock">当值为 <see langword="true"/> 时，此事务访问的所有事务状态
    /// 即使在读取时也会获取排他锁，以避免高争用情况下的锁升级冲突。</param>
    /// <returns>表示事务操作的 <see cref="Task"/>。</returns>
    Task RunTransaction(TransactionOption transactionOption, Func<Task> transactionDelegate, bool useExclusiveLock);

    /// <summary>
    /// 使用指定的事务选项执行委托。
    /// </summary>
    /// <param name="transactionOption">事务的创建、加入或抑制行为。</param>
    /// <param name="transactionDelegate">
    /// 要执行的委托。其结果指示事务是否应提交。
    /// </param>
    /// <param name="useExclusiveLock">当值为 <see langword="true"/> 时，此事务访问的所有事务状态
    /// 即使在读取时也会获取排他锁，以避免高争用情况下的锁升级冲突。</param>
    /// <returns>表示事务操作的 <see cref="Task"/>。</returns>
    Task RunTransaction(TransactionOption transactionOption, Func<Task<bool>> transactionDelegate, bool useExclusiveLock);
}
