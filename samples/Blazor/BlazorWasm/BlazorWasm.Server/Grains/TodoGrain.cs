using Orleans.Runtime;
using BlazorWasm.Models;

namespace BlazorWasm.Grains;

public class TodoGrain(
    ILogger<TodoGrain> logger,
    [PersistentState("State")] IPersistentState<TodoGrain.State> state) : Grain, ITodoGrain
{
    private string GrainType => nameof(TodoGrain);

    private Guid GrainKey => this.GetPrimaryKey();

    public Task<TodoItem?> GetAsync() => Task.FromResult(state.State.Item);

    public async Task SetAsync(TodoItem item)
    {
        // 确保键一致。
        if (item.Key != GrainKey)
        {
            throw new InvalidOperationException();
        }

        // 保存待办事项。
        state.State = state.State with { Item = item };
        await state.WriteStateAsync();

        // 将该事项注册到所属用户的列表中。
        await GrainFactory.GetGrain<ITodoManagerGrain>(item.OwnerKey)
            .RegisterAsync(item.Key);

        // 用于示例调试。
        logger.LogInformation(
            "{@GrainType} {@GrainKey} now contains {@Todo}",
            GrainType, GrainKey, item);

        // 尽力通知侦听器，不作成功保证。
        var streamId = StreamId.Create(nameof(ITodoGrain), item.OwnerKey);
        this.GetStreamProvider("MemoryStreams").GetStream<TodoNotification>(streamId)
            .OnNextAsync(new TodoNotification(item.Key, item))
            .Ignore();
    }

    public async Task ClearAsync()
    {
        // 状态已清除时的快速路径。
        if (state.State.Item is null) return;

        // 保留这些键。
        var itemKey = state.State.Item.Key;
        var ownerKey = state.State.Item.OwnerKey;

        // 从注册表中取消注册。
        await GrainFactory.GetGrain<ITodoManagerGrain>(ownerKey)
            .UnregisterAsync(itemKey);

        // 清除状态。
        await state.ClearStateAsync();

        // 用于示例调试。
        logger.LogInformation(
            "{@GrainType} {@GrainKey} is now cleared",
            GrainType, GrainKey);

        // 尽力通知侦听器，不作成功保证。
        var streamId = StreamId.Create(nameof(ITodoGrain), ownerKey);
        this.GetStreamProvider("MemoryStreams").GetStream<TodoNotification>(streamId)
            .OnNextAsync(new TodoNotification(itemKey, null))
            .Ignore();

        // 无需再保持激活。
        DeactivateOnIdle();
    }

    [GenerateSerializer, Immutable]
    public sealed record class State
    {
        [Id(0)]
        public TodoItem? Item { get; init; }
    }
}
