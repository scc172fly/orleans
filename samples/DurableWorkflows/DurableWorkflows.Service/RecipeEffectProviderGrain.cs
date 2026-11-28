using Orleans.Durable.Journaling;

namespace DurableWorkflows;

// 这个独立的日志化服务提供方使支付和电子邮件协议无需凭据即可运行。在生产环境中，
// 网关仅将服务文档明确保证未产生副作用的失败归类为 ProvenNoEffect，并使用服务提供方的权威查询接口。
public sealed class RecipeEffectProviderGrain : DurableGrain, IRecipeEffectProviderGrain
{
    private readonly IDurableDictionary<string, EffectRequest> _applied;
    private readonly IDurableDictionary<string, ProviderResult> _results;
    private readonly IDurableList<string> _keys;
    private readonly IDurableList<Guid> _attempts;
    private readonly IDurableValue<RecipeProviderBehavior> _behavior;
    private TaskCompletionSource? _heldResponse;

    public RecipeEffectProviderGrain()
    {
        _applied = StateManager.GetOrAddDictionary<string, EffectRequest>("applied");
        _results = StateManager.GetOrAddDictionary<string, ProviderResult>("results");
        _keys = StateManager.GetOrAddList<string>("keys");
        _attempts = StateManager.GetOrAddList<Guid>("attempts");
        _behavior = StateManager.GetOrAddValue<RecipeProviderBehavior>("behavior");
    }

    public async Task<ProviderResult> DispatchAsync(string key, EffectRequest request, Guid attemptId, CancellationToken cancellationToken)
    {
        _keys.Add(key);
        _attempts.Add(attemptId);
        cancellationToken.ThrowIfCancellationRequested();
        if (_behavior.Value is RecipeProviderBehavior.UnavailableBeforeApply)
        {
            await WriteStateAsync(CancellationToken.None);
            throw new IOException("Provider connection ended before an authoritative outcome was received.");
        }

        var repeatable = request.Kind is not EffectKind.Email;
        if (repeatable && _results.TryGetValue(key, out var existing))
        {
            if (_applied.TryGetValue(key, out var previous) && previous != request)
            {
                throw new InvalidOperationException("Provider key conflict.");
            }

            await WriteStateAsync(CancellationToken.None);
            return existing;
        }

        var behavior = _behavior.Value;
        var result = behavior switch
        {
            RecipeProviderBehavior.Reject => new ProviderResult(ProviderOutcome.Rejected, "Provider declined the request."),
            RecipeProviderBehavior.ProveNoEffect => new ProviderResult(ProviderOutcome.ProvenNoEffect, "Provider guarantees the request was not applied."),
            _ => new ProviderResult(ProviderOutcome.Applied, $"Provider receipt {key}.")
        };
        if (result.Outcome is ProviderOutcome.Applied)
        {
            _applied[key] = request;
        }

        if (result.Outcome is not ProviderOutcome.ProvenNoEffect)
        {
            _results[key] = result;
        }

        if (behavior is RecipeProviderBehavior.LoseResponseAfterApply)
        {
            _behavior.Value = RecipeProviderBehavior.Apply;
        }

        await WriteStateAsync(CancellationToken.None);
        if (_heldResponse is { } held)
        {
            await held.Task;
        }

        if (behavior is RecipeProviderBehavior.LoseResponseAfterApply)
        {
            throw new TimeoutException("Provider applied the request; its reply was lost.");
        }

        return result;
    }

    public Task<ProviderResult> LookupAsync(string key, EffectRequest request, CancellationToken cancellationToken)
    {
        if (_behavior.Value is RecipeProviderBehavior.LookupUnavailable)
        {
            throw new IOException("Provider lookup is unavailable.");
        }

        // 此演示中不支持幂等键的邮件服务没有权威的请求查询接口。缺少回执意味着结果未知。
        return Task.FromResult(request.Kind is EffectKind.Email
            ? new ProviderResult(ProviderOutcome.Unknown, "Unkeyed email needs provider support or an operator decision.")
            : _results.TryGetValue(key, out var result) ? result : new ProviderResult(ProviderOutcome.Unknown, "No authoritative outcome is available."));
    }

    public async Task ConfigureAsync(RecipeProviderBehavior behavior)
    {
        _behavior.Value = behavior;
        await WriteStateAsync();
    }

    public Task HoldNextResponseAsync()
    {
        _heldResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return Task.CompletedTask;
    }

    public Task ReleaseResponseAsync()
    {
        _heldResponse?.TrySetResult();
        _heldResponse = null;
        return Task.CompletedTask;
    }

    public async Task<RecipeProviderSnapshot> GetSnapshotAsync()
    {
        await WriteStateAsync();
        return new RecipeProviderSnapshot(new(_applied), [.. _keys], [.. _attempts]);
    }
}
