using System.Collections.Immutable;
using Orleans.EventSourcing;
using Orleans.Providers;
using Orleans.Utilities;

namespace JournaledTodoList.WebApp.Grains;

/// <summary>
/// 基于“状态”的 Journaled Grain。它不保存事件，只保存状态。
/// </summary>
[LogConsistencyProvider(ProviderName = Constants.StateStorageProviderName)]
public sealed class TodoListRegistryGrain(ILogger<TodoListRegistryGrain> logger)
    : JournaledGrain<TodoListRegistryGrain.TodoListRegistry, TodoListReference>
    , ITodoListRegistryGrain
{
    private readonly ObserverManager<ITodoListRegistryObserver> observers = new(
        TimeSpan.FromMinutes(5),
        logger);

    public async Task RegisterTodoListAsync(TodoListReference todoListReference)
    {
        if (State.TodoLists.Contains(todoListReference))
        {
            return;
        }

        RaiseEvent(todoListReference);
        await ConfirmEvents();
        await NotifyObservers();
    }

    // 无需在 TodoListRegistry 中定义 Apply 方法，可以重写
    // TransitionState 方法，并在这里更新状态。
    protected override void TransitionState(TodoListRegistry state, TodoListReference @event)
    {
        // 如果已经存在具有相同 ID 的条目，则使用新条目替换它，
        // 并保持条目的原有顺序。
        var existingList = state.TodoLists.FirstOrDefault(x => x.Id == @event.Id);

        if (existingList is not null)
        {
            state.TodoLists = state.TodoLists.Replace(existingList, @event);
        }
        else
        {
            state.TodoLists = state.TodoLists.Add(@event);
        }
    }

    public Task<ImmutableArray<TodoListReference>> GetAllTodoListsAsync()
    {
        return Task.FromResult(State.TodoLists);
    }

    public Task Subscribe(ITodoListRegistryObserver observer)
    {
        observers.Subscribe(observer, observer);
        observer.OnTodoListsChanged(State.TodoLists);
        return Task.CompletedTask;
    }

    public Task Unsubscribe(ITodoListRegistryObserver observer)
    {
        observers.Unsubscribe(observer);
        return Task.CompletedTask;
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        observers.Clear();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    private Task NotifyObservers()
        => observers.Notify(observer => observer.OnTodoListsChanged(State.TodoLists));

    [GenerateSerializer, Immutable]
    public sealed class TodoListRegistry
    {
        [Id(0)]
        public ImmutableArray<TodoListReference> TodoLists { get; set; } = [];
    }
}
