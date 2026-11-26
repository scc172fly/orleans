using System.Reflection;
using System.Security.Claims;

namespace Orleans.Docs.Snippets.Interceptors;

// <per_grain_filter>
public sealed class MyFilteredGrain
    : Grain, IMyFilteredGrain, IIncomingGrainCallFilter
{
    async Task IIncomingGrainCallFilter.Invoke(
        IIncomingGrainCallContext context)
    {
        await context.Invoke();

        // 电话从7次改为38次。
        if (string.Equals(
            context.InterfaceMethod.Name,
            nameof(IMyFilteredGrain.GetFavoriteNumber),
            StringComparison.Ordinal))
        {
            context.Result = 38;
        }
    }

    public Task<int> GetFavoriteNumber() => Task.FromResult(7);
}
// </per_grain_filter>

// <access_control_contract>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AuthorizeGrainCallAttribute(string policy) : Attribute
{
    public string Policy { get; } = policy;
}

public interface ITrustedCallerIdentityAccessor
{
    // 使用由可信赖的东道主/过滤基础设施确定的身份来实施此功能。
    ClaimsPrincipal? Caller { get; }
}

public sealed class ApplicationAuthorizationService
{
    public const string AdministratorPolicy = "Administrator";

    public ValueTask<bool> AuthorizeAsync(
        ClaimsPrincipal? caller,
        string policy)
    {
        var isAuthorized = policy switch
        {
            AdministratorPolicy =>
                caller?.Identity?.IsAuthenticated == true
                && caller.IsInRole("Administrator"),
            _ => false,
        };

        return ValueTask.FromResult(isAuthorized);
    }
}
// </access_control_contract>

// <access_control_grain>
public sealed class MyAccessControlledGrain(
    ITrustedCallerIdentityAccessor callerIdentity,
    ApplicationAuthorizationService authorizationService)
    : Grain, IAccessControlledGrain, IIncomingGrainCallFilter
{
    async Task IIncomingGrainCallFilter.Invoke(
        IIncomingGrainCallContext context)
    {
        var authorization = context.ImplementationMethod
            .GetCustomAttribute<AuthorizeGrainCallAttribute>();

        if (authorization is not null
            && !await authorizationService.AuthorizeAsync(
                callerIdentity.Caller,
                authorization.Policy))
        {
            throw new UnauthorizedAccessException(
                "The caller isn't authorized to invoke this operation.");
        }

        await context.Invoke();
    }

    [AuthorizeGrainCall(ApplicationAuthorizationService.AdministratorPolicy)]
    public Task<int> GetFavoriteNumber() => Task.FromResult(7);
}
// </access_control_grain>
