// Copyright (c) Microsoft. All rights reserved.
// 持麻省理工学院执照

namespace Orleans.ShoppingCart.Silo.Services;

public sealed class ComponentStateChangedObserver
{
    public event Func<Task>? OnStateChanged;

    public Task NotifyStateChangedAsync() =>
        OnStateChanged?.Invoke() ?? Task.CompletedTask;
}
