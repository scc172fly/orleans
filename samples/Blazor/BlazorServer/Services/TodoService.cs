using System.Collections.Immutable;
using BlazorServer.Models;
using Orleans.Streams;

namespace BlazorServer.Services;

public sealed class TodoService(ILogger<TodoService> logger, IClusterClient client)
{
    public async Task<ImmutableArray<TodoItem>> GetAllAsync(Guid ownerKey)
    {
        // 获取此所有者的全部待办项目密钥
        var itemKeys = await client
            .GetGrain<ITodoManagerGrain>(ownerKey)
            .GetAllAsync();

        // 扇扇扇出以平行获得分组中的单个项目
        // 同时提出所有个别请求
        var tasks = itemKeys
            .Select(async itemId =>
            {
                var item = await client
                    .GetGrain<ITodoGrain>(itemId)
                    .GetAsync();

                // 如果个人 grain 未能解登记,我们就可以取得无效结果
                // 这件事,我们在这里可以完成
                if (item is null)
                {
                    await client
                        .GetGrain<ITodoManagerGrain>(ownerKey)
                        .UnregisterAsync(itemId);
                }

                return item;
            });

        var result = await Task.WhenAll(tasks);

        // 将 null TodoItems 过滤出来,然后以不可变数组返回。
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
