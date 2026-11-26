using System.Collections.Immutable;
using Orleans.EventSourcing;
using Orleans.Providers;
using Orleans.Utilities;

namespace JournaledTodoList.WebApp.Grains;

/// <summary>
/// 以“状态”为基础的《日刊》 Grain。它不会保存事件,只是国家而已。
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

    // 我们可不使用 TodoListRegistry中的“应用方法”,而是可以取代
    // 排 TransitionState 方法, 并在此更新状态 。
    protected override void TransitionState(TodoListRegistry state, TodoListReference @event)
    {
        // 如果现有项目使用同一编号,用新项目替换,
        // 确保项目顺序保持不变。
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
