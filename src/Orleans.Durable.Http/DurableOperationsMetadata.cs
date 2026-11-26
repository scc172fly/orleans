using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Orleans.Durable.Tasks;

namespace Orleans.Durable.Http;

/// <summary>确定了一个终点的持久业务组,并得出了行动的关键和地点。</summary>
/// <param name="scope">集团业务的范围。</param>
/// <remarks>每个组都有自己的实例, 其状态端点的 <see cref="DurableOperationStatusEndpoint"/> 参考文献就是其中的一个。</remarks>
internal sealed class DurableOperationsMetadata(string scope)
{
    private RouteEndpoint? _operationEndpoint;
    private TemplateBinder? _operationBinder;

    /// <summary>获得小组行动的范围</summary>
    public string Scope { get; } = scope;

    /// <summary>返回请求的终点的持久操作组。</summary>
    public static DurableOperationsMetadata Get(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<DurableOperationsMetadata>()
        ?? throw new InvalidOperationException(
            $"Durable operation endpoints belong to a group which WithDurableOperations configures, but '{context.GetEndpoint()?.DisplayName}' doesn't.");

    /// <summary>返回该组的状态端点,其路线模式包括该组的前缀。</summary>
    public RouteEndpoint GetOperationEndpoint(EndpointDataSource endpoints) => _operationEndpoint ??= endpoints.Endpoints
        .OfType<RouteEndpoint>()
        .Single(endpoint => ReferenceEquals(endpoint.Metadata.GetMetadata<DurableOperationStatusEndpoint>()?.Group, this));

    /// <summary>
    /// 返回运行 <paramref name="operationId"/>状态监视器的路径路径,并随请求值返回
    /// 组的路线参数。
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

    /// <summary>返回请求名称的操作 <paramref name="operationId"/> 的密钥。</summary>
    /// <remarks>
    /// 关键是集团的范围、请求中小组路线参数的值、调用者的范围,然后是
    /// 运行标识符,每个部分。
    /// 命令等前缀名称和调用对象的范围,因此两个资源或
    /// 两名来电者, 命名两个操作。
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

/// <summary>使用其调用员的视野和识别资料完成操作的关键。</summary>
internal static class DurableOperationKeys
{
    /// <summary>控制调用者范围的关键段的前缀。</summary>
    private const char CallerPrefix = '@';

    /// <summary>
    /// 返回 <paramref name="prefix"/> ,然后是请求者调用的段次
    /// <see cref="DurableHttpOptions.GetCallerScope"/>和 <paramref name="operationId"/>的部分。
    /// </summary>
    /// <remarks>
    /// 呼叫器的分段是 <c>@</c> 匿名的来电者, <c>@{scope}</c> 使用一个有范围、每个键的呼叫器。
    /// 完全有一个电话段, <see cref="TaskId"/> 将每个段分离出来,因此不同的范围和识别符号
    /// 永远不分享钥匙。
    /// </remarks>
    /// <exception cref="InvalidOperationException">认证的调用人没有范围,也没有空。</exception>
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

/// <summary>标记一个持久作业组的状态终点,该作业组负责确定该组的业务。</summary>
/// <param name="Group">组。</param>
internal sealed record DurableOperationStatusEndpoint(DurableOperationsMetadata Group);
