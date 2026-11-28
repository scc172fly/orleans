// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT License.

using Orleans.Core;

namespace Orleans.ShoppingCart.Grains;

/// <summary>
/// 一个包装 Grain 状态实例的辅助类，确保任意时刻只有一个尚未完成的写入操作。
/// </summary>
/// <param name="state">Grain 状态。</param>
internal sealed class StateManager(IStorage state)
{
    /// <summary>
    /// 允许在后台写入状态。
    /// </summary>
    private Task? _pendingOperation;

    // 可重入 Grain 执行 WriteStateAsync 时，并发写入可能导致 ETag 冲突。
    // 解决办法是将写入串行化并批量合并，确保任意时刻只有一个尚未完成的写入操作。
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
            // 等待尚未完成的写入，但忽略其结果，因为它不包含本次请求的变更。
            await currentWriteStateOperation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            if (_pendingOperation == currentWriteStateOperation)
            {
                // 仅当当前尚未完成的操作与我们刚等待的操作相同时，才将其置为 null，否则
                // 另一个请求可能已经完成了这一操作。
                _pendingOperation = null;
            }
        }

        Task operation;
        if (_pendingOperation is null)
        {
            // 如果初始写入完成后，没有其他请求发起新的写入操作，则现在发起写入。
            operation = performOperation(state);
            _pendingOperation = operation;
        }
        else
        {
            // 如果已有多个持久化状态的请求排队，则无需为每个请求都安排新的写入
            // 操作，因为初始写入（我们已经等待过）之后的任何写入都会包含累积的
            // 变更，包括本次调用者请求的变更。只需等待新的尚未完成的写入即可。
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
                // 仅当当前尚未完成的操作与我们刚等待的操作相同时，才将其置为 null，否则
                // 另一个请求可能已经完成了这一操作。
                _pendingOperation = null;
            }
        }
    }
}
