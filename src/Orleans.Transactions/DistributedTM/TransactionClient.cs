using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

using Orleans.Serialization;

namespace Orleans.Transactions;

internal class TransactionClient : ITransactionClient
{
    private readonly ITransactionAgent _transactionAgent;
    private readonly Serializer<OrleansTransactionAbortedException> _serializer;

    public TransactionClient(ITransactionAgent transactionAgent, Serializer<OrleansTransactionAbortedException> serializer)
    {
        _transactionAgent = transactionAgent;
        _serializer = serializer;
    }

    public Task RunTransaction(TransactionOption transactionOption, Func<Task> transactionDelegate)
        => RunTransaction(transactionOption, transactionDelegate, useExclusiveLock: false);

    public Task RunTransaction(TransactionOption transactionOption, Func<Task<bool>> transactionDelegate)
        => RunTransaction(transactionOption, transactionDelegate, useExclusiveLock: false);

    public async Task RunTransaction(TransactionOption transactionOption, Func<Task> transactionDelegate, bool useExclusiveLock)
    {
        if (transactionDelegate is null)
        {
            throw new ArgumentNullException(nameof(transactionDelegate));
        }

        await RunTransaction(transactionOption, async () =>
        {
            await transactionDelegate();
            return true;
        }, useExclusiveLock);
    }

    public async Task RunTransaction(TransactionOption transactionOption, Func<Task<bool>> transactionDelegate, bool useExclusiveLock)
    {
        if (transactionDelegate is null)
        {
            throw new ArgumentNullException(nameof(transactionDelegate));
        }

        // 获取环境事务上下文。
        var ambientTransactionInfo = TransactionContext.GetTransactionInfo();

        if (ambientTransactionInfo is not null && transactionOption == TransactionOption.Suppress)
        {
            throw new NotSupportedException("Delegate cannot be executed within a transaction.");
        }

        if (ambientTransactionInfo is null && transactionOption == TransactionOption.Join)
        {
            throw new NotSupportedException("Delegate cannot be executed outside of a transaction.");
        }

        try
        {
            switch (transactionOption)
            {
                case TransactionOption.Create:
                    await RunDelegateWithTransaction(null, transactionDelegate, useExclusiveLock);
                    break;
                case TransactionOption.Join:
                    await RunDelegateWithTransaction(ambientTransactionInfo, transactionDelegate, useExclusiveLock);
                    break;
                case TransactionOption.CreateOrJoin:
                    await RunDelegateWithTransaction(ambientTransactionInfo, transactionDelegate, useExclusiveLock);
                    break;
                case TransactionOption.Suppress:
                    await RunDelegateWithSupressedTransaction(ambientTransactionInfo, transactionDelegate);
                    break;
                case TransactionOption.Supported:
                    await RunDelegateWithSupportedTransaction(ambientTransactionInfo, transactionDelegate);
                    break;
                case TransactionOption.NotAllowed:
                    await RunDelegateWithDisallowedTransaction(ambientTransactionInfo, transactionDelegate);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(transactionOption), $"{transactionOption} is not supported");
            }
        }
        finally
        {
            // 恢复环境事务上下文（如果存在）。
            TransactionContext.SetTransactionInfo(ambientTransactionInfo);
        }
    }

    private static async Task RunDelegateWithDisallowedTransaction(TransactionInfo? ambientTransactionInfo, Func<Task<bool>> transactionDelegate)
    {
        if (ambientTransactionInfo is not null)
        {
            // 委托内不允许存在事务。
            throw new NotSupportedException("Delegate cannot be executed within a transaction.");
        }

        // 执行委托。
        _ = await transactionDelegate();
    }

    private static async Task RunDelegateWithSupportedTransaction(TransactionInfo? ambientTransactionInfo, Func<Task<bool>> transactionDelegate)
    {
        if (ambientTransactionInfo is null)
        {
            // 执行委托。
            _ = await transactionDelegate();
        }
        else
        {
            // 执行委托。
            ambientTransactionInfo.TryToCommit = await transactionDelegate();
        }
    }

    private static async Task RunDelegateWithSupressedTransaction(TransactionInfo? ambientTransactionInfo, Func<Task<bool>> transactionDelegate)
    {
        // 清除事务上下文。
        TransactionContext.Clear();

        if (ambientTransactionInfo is null)
        {
            // 执行委托。
            _ = await transactionDelegate();
        }
        else
        {
            // 执行委托。
            ambientTransactionInfo.TryToCommit = await transactionDelegate();
        }
    }

    private async Task RunDelegateWithTransaction(TransactionInfo? ambientTransactionInfo, Func<Task<bool>> transactionDelegate, bool useExclusiveLock)
    {
        TransactionInfo transactionInfo;

        if (ambientTransactionInfo is null)
        {
            // TODO：这里应该使用可配置参数。
            var transactionTimeout = Debugger.IsAttached ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(10);

            // 启动事务。
            transactionInfo = await _transactionAgent.StartTransaction(readOnly: false, transactionTimeout);
        }
        else
        {
            // 为环境事务创建分支。
            transactionInfo = ambientTransactionInfo.Fork();
        }

        // 如果有要求，则设置排他锁标志。
        if (useExclusiveLock)
        {
            transactionInfo.UseExclusiveLock = true;
        }

        // 设置事务上下文。
        TransactionContext.SetTransactionInfo(transactionInfo);

        try
        {
            // 执行委托。
            transactionInfo.TryToCommit = await transactionDelegate();
        }
        catch (Exception exception)
        {
            // 在事务中记录异常。
            transactionInfo.RecordException(exception, _serializer);
        }

        // 将待处理操作汇总到事务中。
        transactionInfo.ReconcilePending();

        if (ambientTransactionInfo is null)
        {
            // 由于没有可加入的环境事务，因此结束当前事务。
            await FinalizeTransaction(transactionInfo);
        }
        else
        {   // 将事务与环境事务合并。
            ambientTransactionInfo.Join(transactionInfo);
        }
    }

    private async Task FinalizeTransaction(TransactionInfo transactionInfo)
    {
        // 准备处理异常（如果存在）。
        OrleansTransactionException? transactionException;

        // 检查事务是否等待中止。
        transactionException = transactionInfo.MustAbort(_serializer);

        if (transactionException is not null || transactionInfo.TryToCommit is false)
        {
            // 事务等待中止。
            await _transactionAgent.Abort(transactionInfo);
        }
        else
        {
            // 尝试完成事务决议。
            var (status, exception) = await _transactionAgent.Resolve(transactionInfo);

            if (status != TransactionalStatus.Ok)
            {
                // 事务决议失败。
                transactionException = status.ConvertToUserException(transactionInfo.Id, exception);
                ExceptionDispatchInfo.SetCurrentStackTrace(transactionException);
            }
        }

        if (transactionException != null)
        {
            // 事务失败，将异常向上传播。
            ExceptionDispatchInfo.Throw(transactionException);
        }
    }
}
