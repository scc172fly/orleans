// Copyright (c) Microsoft. All rights reserved.
// 持麻省理工学院执照

using Orleans.Core;

namespace Orleans.ShoppingCart.Grains;

/// <summary>
/// 辅助类,该类包扎 grain 国家实例并确保在任何时间的任何时候只有一个未完成的写作操作。
/// </summary>
/// <param name="state">下 grain 状态。</param>
internal sealed class StateManager(IStorage state)
{
    /// <summary>
    /// 允许在背景中进行状态写入 。
    /// </summary>
    private Task? _pendingOperation;

    // 当Reentrant grain 正在做 WriteStateAsync时,由于同时写作,有可能出现侵权行为。
    // 解决问题的方法就是进行序列化和批量写作,确保在任何时候只收到一份单封未完成的书。
    public async ValueTask WriteStateAsync()
    {
        await PerformOperationAsync(static state => state.WriteStateAsync());
    }

    public async ValueTask ClearStateAsync()
    {
        await PerformOperationAsync(static state => state.ClearStateAsync());
    }

    public async ValueTask PerformOperationAsync(Func<IStorage, Task> performOperation)
    {
        if (_pendingOperation is Task currentWriteStateOperation)
        {
            // 等待着未完成的写作, 但忽略它, 因为其中不包括我们的更改
            await currentWriteStateOperation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            if (_pendingOperation == currentWriteStateOperation)
            {
                // 唯一能取消未完成行动 条件是它和我们期待的一样 否则
                // 另一项请求可能已经提出。
                _pendingOperation = null;
            }
        }

        Task operation;
        if (_pendingOperation is null)
        {
            // 如果在初始写作完成后,没有其他请求启动新的写作操作,请立即进行。
            operation = performOperation(state);
            _pendingOperation = operation;
        }
        else
        {
            // 如果有许多要求被排在了持续状态的呼声中,就没有理由写新书
            // 每个操作的操作, 因为任何写作( 之前我们期待的首部) 都将累积
            // 包括我们打电话者要求的更改。 等待新的未写 。
            operation = _pendingOperation;
        }

        try
        {
            await operation;
        }
        finally
        {
            if (_pendingOperation == operation)
            {
                // 唯一能取消未完成行动 条件是它和我们期待的一样 否则
                // 另一项请求可能已经提出。
                _pendingOperation = null;
            }
        }
    }
}
