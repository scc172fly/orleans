using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Durable.Http;

namespace Microsoft.AspNetCore.Builder;

/// <summary>根据 Nexus HTTP 协议映射 Nexus 服务，其操作用于启动及取消持久化工作。</summary>
/// <remarks>
/// <para>
/// Nexus 是 Temporal 跨命名空间操作的协议：调用方通过
/// <c>POST {endpoint}/{service}/{operation}</c> 启动操作，并通过 <c>POST {endpoint}/{service}/{operation}/cancel</c>
/// 取消操作，使用启动时返回的令牌寻址。参见
/// <see href="https://github.com/nexus-rpc/api/blob/main/SPEC.md">Nexus HTTP 规范</see>。
/// </para>
/// <para>
/// Nexus 操作也是持久化操作，与
/// <see cref="DurableHttpEndpointRouteBuilderExtensions.WithDurableOperations"/> 配置的操作相同：调用方的
/// <c>Nexus-Request-Id</c> 在服务作用域及调用方作用域（由
/// <see cref="DurableHttpOptions.GetCallerScope"/> 返回）内标识操作。具有相同作用域且无路由参数的组可寻址同一组操作，因此能够流式传输
/// 操作状态或传递信号。身份验证及授权由应用负责，应用应在
/// 服务端点上配置相应机制。
/// </para>
/// </remarks>
public static class NexusEndpointRouteBuilderExtensions
{
    // 处理程序以 Minimal API 处理程序的方式绑定请求，Minimal API 使用反射分析处理程序。
    private const string HandlerBindingUnreferencedCode =
        "This API may perform reflection on the supplied delegate and its parameters. These types may be trimmed if not directly referenced.";
    private const string HandlerBindingDynamicCode =
        "This API may perform reflection on the supplied delegate and its parameters. These types may require generated code and aren't compatible with native AOT applications.";

    /// <summary>映射 Nexus 服务 <paramref name="service"/>，其操作由 <see cref="MapNexusOperation"/> 映射。</summary>
    /// <param name="endpoints">端点路由构建器，其前缀为 Nexus 端点，例如 <c>/nexus</c>。</param>
    /// <param name="service">由 URL 非保留字符组成的服务名称，例如 <c>payments.v1</c>。</param>
    /// <param name="scope">
    /// 服务操作的作用域；为 <see langword="null"/> 时使用 <c>nexus/{service}</c>。
    /// </param>
    /// <returns>用于映射服务操作并配置端点的组构建器。</returns>
    /// <exception cref="ArgumentException"><paramref name="service"/> 为空，或包含 URL 非保留字符之外的字符。</exception>
    public static RouteGroupBuilder MapNexusService(this IEndpointRouteBuilder endpoints, string service, string? scope = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        DurableTaskEndpointConventions.ThrowIfInvalidName(service, nameof(service));
        return endpoints.MapGroup($"/{service}").WithMetadata(new NexusServiceMetadata(scope ?? $"nexus/{service}"));
    }

    /// <summary>
    /// 映射 Nexus 操作 <paramref name="operation"/>，其处理程序返回持久化工作，例如
    /// <c>grain.ChargeAsync(input)</c>；启动请求会将该工作作为操作启动。
    /// </summary>
    /// <param name="service"><see cref="MapNexusService"/> 返回的组。</param>
    /// <param name="operation">由 URL 非保留字符组成的操作名称，例如 <c>charge</c>。</param>
    /// <param name="handler">
    /// 一个委托，像 Minimal API 处理程序一样绑定操作输入（例如来自 JSON 请求体）
    /// 及其他参数，并返回持久化 Grain 调用或对现有任务的引用（由
    /// <c>grain.GetDurableTask&lt;T&gt;(id)</c> 返回），可直接返回，也可通过 <see cref="Task{TResult}"/> 或
    /// <see cref="ValueTask{TResult}"/> 返回。每次启动请求（包括重试）都会运行该委托，但它仅创建工作。
    /// </param>
    /// <returns>用于配置操作的启动端点和取消端点的组构建器。</returns>
    /// <remarks>
    /// <para>
    /// <c>POST {service}/{operation}</c> 将工作作为由请求的 <c>Nexus-Request-Id</c> 命名的操作启动；
    /// 如果请求未提供该标识符，则启动新操作，并按如下方式响应：
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// 返回 <c>200 OK</c> 及结果和 <c>Nexus-Operation-State: succeeded</c>（操作成功时），例如
    /// 操作完成后收到重试请求的情况。
    /// </description></item>
    /// <item><description><c>201 Created</c> 及 <c>OperationInfo</c>（其令牌用于寻址操作），在操作运行期间返回。</description></item>
    /// <item><description>
    /// 操作失败或被取消时返回 <c>424 Failed Dependency</c> 及 <c>OperationError</c> 失败信息，其中消息由
    /// <see cref="DurableHttpOptions.CreateError"/> 描述。若执行工作的 Grain 拒绝工作，则操作
    /// 以 <see cref="DurableOperationError.NotStartedCode"/> 错误码失败；若执行工作的 Grain
    /// 未确认工作（此时工作可能已运行），则以
    /// <see cref="DurableOperationError.OutcomeUnknownCode"/> 错误码失败。
    /// </description></item>
    /// <item><description>
    /// 处理程序错误：请求标识符无效时返回 <c>400</c> <c>BAD_REQUEST</c>；若请求标识符重复用于其他工作，则返回 <c>409</c> <c>CONFLICT</c>，
    /// 若请求未到达操作，则返回 <c>503</c> <c>UNAVAILABLE</c>，
    /// 此时操作保持不变，调用方重试。
    /// </description></item>
    /// </list>
    /// <para>
    /// <c>POST {service}/{operation}/cancel</c> 持久化地请求取消由其
    /// <c>Nexus-Operation-Token</c> 请求头或 <c>token</c> 查询参数指定的操作，并返回 <c>202 Accepted</c>，
    /// 对终态操作亦如此，且不改变操作。若令牌未指定任何操作，则返回 <c>404</c> <c>NOT_FOUND</c>
    /// 处理程序错误。<see cref="INexusOperationTokenFormat"/> 用于格式化令牌。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="operation"/> 为空或包含 URL 非保留字符以外的字符，或者 <paramref name="handler"/>
    /// 不返回持久化工作。
    /// </exception>
    [RequiresUnreferencedCode(HandlerBindingUnreferencedCode)]
    [RequiresDynamicCode(HandlerBindingDynamicCode)]
    public static RouteGroupBuilder MapNexusOperation(this IEndpointRouteBuilder service, string operation, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(handler);
        DurableTaskEndpointConventions.ThrowIfInvalidName(operation, nameof(operation));
        var resultType = DurableTaskEndpointConventions.GetResultType(handler.Method)
            ?? throw new ArgumentException(
                $"A Nexus operation's handler returns DurableTask or DurableTask<TResult>, not '{handler.Method.ReturnType}'.",
                nameof(handler));

        var group = service.MapGroup($"/{operation}");
        var start = group.MapPost("/", handler)
            .AddEndpointFilter(static async (context, next) =>
            {
                var httpContext = context.HttpContext;
                return await next(context) is DurableTask work
                    ? await httpContext.RequestServices.GetRequiredService<DurableNexusOperations>().StartAsync(httpContext, work)
                    : throw new InvalidOperationException($"The handler of '{httpContext.GetEndpoint()?.DisplayName}' returned no durable work.");
            });
        start.Add(static endpoint =>
        {
            for (var index = endpoint.Metadata.Count - 1; index >= 0; index--)
            {
                if (endpoint.Metadata[index] is IProducesResponseTypeMetadata { Type: { } type } && typeof(DurableTask).IsAssignableFrom(type))
                {
                    endpoint.Metadata.RemoveAt(index);
                }
            }
        });

        if (resultType == typeof(void))
        {
            start.Produces(StatusCodes.Status200OK);
        }
        else
        {
            start.Produces(StatusCodes.Status200OK, resultType);
        }

        start.Produces(StatusCodes.Status201Created, typeof(void), "application/json")
            .Produces(StatusCodes.Status424FailedDependency, typeof(void), "application/json");
        group.MapPost(
            "/cancel",
            static context => DurableHttpEndpointRouteBuilderExtensions.ExecuteAsync(
                context,
                context.RequestServices.GetRequiredService<DurableNexusOperations>().CancelAsync(context)));
        return group;
    }
}
