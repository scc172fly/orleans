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

        // 抓取环境交易背景
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
            // 恢复环境交易环境,如果有的话
            TransactionContext.SetTransactionInfo(ambientTransactionInfo);
        }
    }

    private static async Task RunDelegateWithDisallowedTransaction(TransactionInfo? ambientTransactionInfo, Func<Task<bool>> transactionDelegate)
    {
        if (ambientTransactionInfo is not null)
        {
            // 代表内部不允许交易
            throw new NotSupportedException("Delegate cannot be executed within a transaction.");
        }

        // 运行代表
        _ = await transactionDelegate();
    }

    private static async Task RunDelegateWithSupportedTransaction(TransactionInfo? ambientTransactionInfo, Func<Task<bool>> transactionDelegate)
    {
        if (ambientTransactionInfo is null)
        {
            // 运行代表
            _ = await transactionDelegate();
        }
        else
        {
            // 运行代表
            ambientTransactionInfo.TryToCommit = await transactionDelegate();
        }
    }

    private static async Task RunDelegateWithSupressedTransaction(TransactionInfo? ambientTransactionInfo, Func<Task<bool>> transactionDelegate)
    {
        // 清晰交易环境
        TransactionContext.Clear();

        if (ambientTransactionInfo is null)
        {
            // 运行代表
            _ = await transactionDelegate();
        }
        else
        {
            // 运行代表
            ambientTransactionInfo.TryToCommit = await transactionDelegate();
        }
    }

    private async Task RunDelegateWithTransaction(TransactionInfo? ambientTransactionInfo, Func<Task<bool>> transactionDelegate, bool useExclusiveLock)
    {
        TransactionInfo transactionInfo;

        if (ambientTransactionInfo is null)
        {
            // TODO: 这应该是可配置参数
            var transactionTimeout = Debugger.IsAttached ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(10);

            // 启动交易
            transactionInfo = await _transactionAgent.StartTransaction(readOnly: false, transactionTimeout);
        }
        else
        {
            // 叉环环境交易
            transactionInfo = ambientTransactionInfo.Fork();
        }

        // 如果请求, 应用独家锁定标记
        if (useExclusiveLock)
        {
            transactionInfo.UseExclusiveLock = true;
        }

        // 设定交易环境
        TransactionContext.SetTransactionInfo(transactionInfo);

        try
        {
            // 运行代表
            transactionInfo.TryToCommit = await transactionDelegate();
        }
        catch (Exception exception)
        {
            // 记录交易的例外
            transactionInfo.RecordException(exception, _serializer);
        }

        // 将待决行动收集到交易中
        transactionInfo.ReconcilePending();

        if (ambientTransactionInfo is null)
        {
            // 由于没有环境交易可加入,所以最终完成交易
            await FinalizeTransaction(transactionInfo);
        }
        else
        {   // 与环境交易合并交易
            ambientTransactionInfo.Join(transactionInfo);
        }
    }

    private async Task FinalizeTransaction(TransactionInfo transactionInfo)
    {
        // 准备例外,如果有
        OrleansTransactionException? transactionException;

        // 检查交易是否尚未中止
        transactionException = transactionInfo.MustAbort(_serializer);

        if (transactionException is not null || transactionInfo.TryToCommit is false)
        {
            // 交易正在中止, 尚未中止
            await _transactionAgent.Abort(transactionInfo);
        }
        else
        {
            // 尝试解决交易
            var (status, exception) = await _transactionAgent.Resolve(transactionInfo);

            if (status != TransactionalStatus.Ok)
            {
                // 解析交易失败
                transactionException = status.ConvertToUserException(transactionInfo.Id, exception);
                ExceptionDispatchInfo.SetCurrentStackTrace(transactionException);
            }
        }

        if (transactionException != null)
        {
            // 交易失败 - 泡沫上浮例外
            ExceptionDispatchInfo.Throw(transactionException);
        }
    }
}
