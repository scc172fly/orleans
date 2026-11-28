using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Orleans.Durable.Tasks;

namespace Orleans.Durable.Http;

/// <summary>标识端点所属的持久化操作组，并派生其操作的键及位置。</summary>
/// <param name="scope">组内操作的作用域。</param>
/// <remarks>每个组都有自己的实例，由其状态端点的 <see cref="DurableOperationStatusEndpoint"/> 引用。</remarks>
internal sealed class DurableOperationsMetadata(string scope)
{
    private RouteEndpoint? _operationEndpoint;
    private TemplateBinder? _operationBinder;

    /// <summary>获取组内操作的作用域。</summary>
    public string Scope { get; } = scope;

    /// <summary>返回请求端点所属的持久化操作组。</summary>
    public static DurableOperationsMetadata Get(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<DurableOperationsMetadata>()
        ?? throw new InvalidOperationException(
            $"Durable operation endpoints belong to a group which WithDurableOperations configures, but '{context.GetEndpoint()?.DisplayName}' doesn't.");

    /// <summary>返回组的状态端点，其路由模式包含组前缀。</summary>
    public RouteEndpoint GetOperationEndpoint(EndpointDataSource endpoints) => _operationEndpoint ??= endpoints.Endpoints
        .OfType<RouteEndpoint>()
        .Single(endpoint => ReferenceEquals(endpoint.Metadata.GetMetadata<DurableOperationStatusEndpoint>()?.Group, this));

    /// <summary>
    /// 返回操作 <paramref name="operationId"/> 的状态监视器路径，使用请求中的
    /// 组路由参数值。
    /// </summary>
    public string? GetOperationPath(HttpContext context, EndpointDataSource endpoints, TemplateBinderFactory binders, string operationId)
    {
        var endpoint = GetOperationEndpoint(endpoints);
        var values = new RouteValueDictionary();
        foreach (var parameter in endpoint.RoutePattern.Parameters)
        {
            if (context.Request.RouteValues.TryGetValue(parameter.Name, out var value))
            {
                values[parameter.Name] = value;
            }
        }

        values[DurableHttpOperations.OperationIdParameter] = operationId;
        return (_operationBinder ??= binders.Create(endpoint.RoutePattern)).BindValues(values);
    }

    /// <summary>返回请求指定的操作 <paramref name="operationId"/> 的键。</summary>
    /// <remarks>
    /// 键由组作用域、请求的组路由参数值、调用方作用域，以及
    /// 操作标识符组成，各自作为独立的段。路由参数值将操作限定到组前缀所指定的资源
    /// （例如订单），调用方作用域将其限定到调用方，因此两个资源或
    /// 两个调用方使用相同标识符时，也会指向两个不同的操作。
    /// </remarks>
    public string GetKey(HttpContext context, EndpointDataSource endpoints, DurableHttpOptions options, string operationId)
    {
        var key = TaskId.CreateRoot(Scope);
        foreach (var parameter in GetOperationEndpoint(endpoints).RoutePattern.Parameters)
        {
            if (parameter.Name != DurableHttpOperations.OperationIdParameter)
            {
                key = key.Child(GetRouteValue(context, parameter.Name));
            }
        }

        return DurableOperationKeys.Complete(key, context, options, operationId);
    }

    private static string GetRouteValue(HttpContext context, string name) =>
        Convert.ToString(context.Request.RouteValues[name], CultureInfo.InvariantCulture) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"The request to '{context.GetEndpoint()?.DisplayName}' has no value for '{name}', a route parameter of its group's operations.");
}

/// <summary>将调用方作用域和操作标识符添加到操作键，形成完整的键。</summary>
internal static class DurableOperationKeys
{
    /// <summary>包含调用方作用域的键段前缀。</summary>
    private const char CallerPrefix = '@';

    /// <summary>
    /// 返回 <paramref name="prefix"/>，其后为请求调用方对应的段（由
    /// <see cref="DurableHttpOptions.GetCallerScope"/> 提供）及 <paramref name="operationId"/> 对应的段。
    /// </summary>
    /// <remarks>
    /// 匿名调用方的路径段为 <c>@</c>，有作用域的调用方则为 <c>@{scope}</c>。每个键
    /// 恰好包含一个调用方段，且 <see cref="TaskId"/> 对各段进行转义，因此不同作用域和标识符
    /// 不会共享同一个键。
    /// </remarks>
    /// <exception cref="InvalidOperationException">已通过身份验证的调用方没有作用域，或其作用域为空。</exception>
    public static string Complete(TaskId prefix, HttpContext context, DurableHttpOptions options, string operationId) =>
        prefix.Child(GetCallerSegment(context, options)).Child(operationId).ToString();

    private static string GetCallerSegment(HttpContext context, DurableHttpOptions options)
    {
        var scope = options.GetCallerScope(context);
        if (scope is null)
        {
            return context.User.Identity?.IsAuthenticated == true
                ? throw new InvalidOperationException(
                    $"The request to '{context.GetEndpoint()?.DisplayName}' has an authenticated caller without a scope, so its operations would be shared with anonymous callers. Configure DurableHttpOptions.GetCallerScope to return the scope of each authenticated caller.")
                : CallerPrefix.ToString();
        }

        return scope.Length > 0
            ? string.Concat(CallerPrefix.ToString(), scope)
            : throw new InvalidOperationException("DurableHttpOptions.GetCallerScope returned an empty scope. A caller's scope has at least one character.");
    }
}

/// <summary>标记持久化操作组的状态端点，该端点用于定位组内操作。</summary>
/// <param name="Group">路由组。</param>
internal sealed record DurableOperationStatusEndpoint(DurableOperationsMetadata Group);
