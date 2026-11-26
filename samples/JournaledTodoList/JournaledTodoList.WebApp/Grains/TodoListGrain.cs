using System.Collections.Immutable;
using JournaledTodoList.WebApp.Grains.Events;
using Orleans.EventSourcing;

namespace JournaledTodoList.WebApp.Grains;

public sealed class TodoListGrain : JournaledGrain<TodoListGrain.TodoListProjection, TodoListEvent>, ITodoListGrain
{
    public async Task<ImmutableArray<TodoListEvent>> GetHistoryAsync()
    {
        var events = await RetrieveConfirmedEvents(0, Version);
        return events.ToImmutableArray();
    }

    public async Task<TodoList?> GetTodoListAtTimestampAsync(DateTimeOffset timestamp)
    {
        // 将所有事件都提高到当前版本
        var allEvents = await GetHistoryAsync();

        // 创建新投影并应用过滤事件
        var historicalProjection = new TodoListProjection();
        foreach (var evt in allEvents.Where(e => e.Timestamp <= timestamp))
        {
            TransitionState(historicalProjection, evt);
        }

        // 只有发生与时间戳相匹配的事件时,才会返回 TodoList 。
        return historicalProjection.Timestamp > DateTimeOffset.MinValue
            ? new TodoList(
                Name: this.GetPrimaryKeyString(),
                Items: historicalProjection.Items.Values.OrderBy(x => x.Id).ToImmutableArray(),
                Timestamp: historicalProjection.Timestamp)
            : null;
    }

    public async Task<TodoList> GetTodoListAsync()
    {
        var list = new TodoList(
            Name: this.GetPrimaryKeyString(),
            Items: State.Items.Values.OrderBy(x => x.Id).ToImmutableArray(),
            Timestamp: State.Timestamp);

        return list;
    }

    public async Task AddTodoItemAsync(string title)
    {
        var evt = new TodoItemAdded(
            Version,
            DateTimeOffset.UtcNow,
            title);

        RaiseEvent(evt);
        await ConfirmEvents();
    }

    public async Task UpdateTodoItemAsync(int id, string title)
    {
        var evt = new TodoItemUpdated(id, DateTimeOffset.UtcNow, title);
        RaiseEvent(evt);
        await ConfirmEvents();
    }

    public async Task ToggleTodoItemAsync(int id)
    {
        var evt = new TodoItemToggled(id, DateTimeOffset.UtcNow);
        RaiseEvent(evt);
        await ConfirmEvents();
    }

    public async Task RemoveTodoItemAsync(int id)
    {
        var evt = new TodoItemRemoved(id, DateTimeOffset.UtcNow);
        RaiseEvent(evt);
        await ConfirmEvents();
    }

    public async Task SetNameAsync(string listName)
    {
        var evt = new TodoListNameChanged(listName, DateTimeOffset.UtcNow);
        RaiseEvent(evt);
        await ConfirmEvents();

        // 发布新待办事宜列表列表,并新增待办事宜列表登记处 。
        // 登记处只关心名单的识别号和名称,
        // 因此这是我们需要与登记处互动的唯一地方。
        var registry = GrainFactory.GetGrain<ITodoListRegistryGrain>(Constants.TodoListRegistryId);
        await registry.RegisterTodoListAsync(new TodoListReference(this.GetPrimaryKeyString(), listName));
    }

    /// <summary>
    /// <see cref="TodoListGrain"/>的国家集装箱。
    /// 注:必须是一个可变物体。
    /// </summary>
    public sealed class TodoListProjection
    {
        public Dictionary<int, TodoItem> Items { get; set; } = [];

        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.MinValue;

        public string Name { get; set; } = string.Empty;

        public void Apply(TodoItemAdded added)
        {
            Items.Add(added.ItemId, new TodoItem(added.ItemId, added.Title, false));
            Timestamp = added.Timestamp;
        }

        public void Apply(TodoItemUpdated updated)
        {
            if (Items.TryGetValue(updated.ItemId, out var item))
            {
                Items[updated.ItemId] = item with { Title = updated.Title };
            }
            Timestamp = updated.Timestamp;
        }

        public void Apply(TodoItemToggled toggled)
        {
            if (Items.TryGetValue(toggled.ItemId, out var item))
            {
                Items[toggled.ItemId] = item with { IsCompleted = !item.IsCompleted };
            }
            Timestamp = toggled.Timestamp;
        }

        public void Apply(TodoItemRemoved removed)
        {
            Items.Remove(removed.ItemId);
            Timestamp = removed.Timestamp;
        }

        public void Apply(TodoListNameChanged nameChanged)
        {
            Name = nameChanged.Name;
            Timestamp = nameChanged.Timestamp;
        }
    }
}
