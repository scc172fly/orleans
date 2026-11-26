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
        // 确保关键键前后一致
        if (item.Key != GrainKey)
        {
            throw new InvalidOperationException();
        }

        // 保存项目
        state.State = state.State with { Item = item };
        await state.WriteStateAsync();

        // 将物品及其所有人名单登记在登记册上
        await GrainFactory.GetGrain<ITodoManagerGrain>(item.OwnerKey)
            .RegisterAsync(item.Key);

        // 用于样本调试
        logger.LogInformation(
            "{@GrainType} {@GrainKey} now contains {@Todo}",
            GrainType, GrainKey, item);

        // 通知听众 - 仅尽力
        var streamId = StreamId.Create(nameof(ITodoGrain), item.OwnerKey);
        this.GetStreamProvider("MemoryStreams").GetStream<TodoNotification>(streamId)
            .OnNextAsync(new TodoNotification(item.Key, item))
            .Ignore();
    }

    public async Task ClearAsync()
    {
        // 已经清除状态的快路径
        if (state.State.Item is null) return;

        // 抓稳钥匙
        var itemKey = state.State.Item.Key;
        var ownerKey = state.State.Item.OwnerKey;

        // 登记册的未登记者
        await GrainFactory.GetGrain<ITodoManagerGrain>(ownerKey)
            .UnregisterAsync(itemKey);

        // 清除状态
        await state.ClearStateAsync();

        // 用于样本调试
        logger.LogInformation(
            "{@GrainType} {@GrainKey} is now cleared",
            GrainType, GrainKey);

        // 通知听众 - 仅尽力
        var streamId = StreamId.Create(nameof(ITodoGrain), ownerKey);
        this.GetStreamProvider("MemoryStreams").GetStream<TodoNotification>(streamId)
            .OnNextAsync(new TodoNotification(itemKey, null))
            .Ignore();

        // 没必要再活着了
        DeactivateOnIdle();
    }

    [GenerateSerializer, Immutable]
    public sealed record class State
    {
        [Id(0)]
        public TodoItem? Item { get; init; }
    }
}
