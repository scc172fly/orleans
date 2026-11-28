using System.Collections.Immutable;
using BlazorServer.Models;
using Orleans.Streams;

namespace BlazorServer.Services;

public sealed class TodoService(ILogger<TodoService> logger, IClusterClient client)
{
    public async Task<ImmutableArray<TodoItem>> GetAllAsync(Guid ownerKey)
    {
        // 获取此用户的所有待办事项键。
        var itemKeys = await client
            .GetGrain<ITodoManagerGrain>(ownerKey)
            .GetAllAsync();

        // 扇出调用，并行从集群获取各个事项。
        // 同时发起所有单独的请求。
        var tasks = itemKeys
            .Select(async itemId =>
            {
                var item = await client
                    .GetGrain<ITodoGrain>(itemId)
                    .GetAsync();

                // 如果对应 Grain 未能取消注册，可能会得到 null 结果。
                // 此时可以在这里完成清理工作。
                if (item is null)
                {
                    await client
                        .GetGrain<ITodoManagerGrain>(ownerKey)
                        .UnregisterAsync(itemId);
                }

                return item;
            });

        var result = await Task.WhenAll(tasks);

        // 过滤 null 的 TodoItem，并以不可变数组返回。
        return result
            .OfType<TodoItem>()
            .ToImmutableArray();
    }

    public Task SetAsync(TodoItem item) =>
        client.GetGrain<ITodoGrain>(item.Key).SetAsync(item);

    public Task DeleteAsync(Guid itemKey) =>
        client.GetGrain<ITodoGrain>(itemKey).ClearAsync();

    public Task<StreamSubscriptionHandle<TodoNotification>> SubscribeAsync(
        Guid ownerKey, Func<TodoNotification, Task> action) =>
        client.GetStreamProvider("MemoryStreams")
            .GetStream<TodoNotification>(ownerKey)
            .SubscribeAsync(new TodoItemObserver(logger, action));
}

sealed file class TodoItemObserver(
    ILogger<TodoService> logger,
    Func<TodoNotification, Task> action) : IAsyncObserver<TodoNotification>
{
    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex)
    {
        logger.LogError(ex, ex.Message);
        return Task.CompletedTask;
    }

    public Task OnNextAsync(
        TodoNotification item,
        StreamSequenceToken? token = null) =>
        action(item);
}
