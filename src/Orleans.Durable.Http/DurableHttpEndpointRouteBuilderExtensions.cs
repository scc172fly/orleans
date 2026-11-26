using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Durable.Http;

namespace Microsoft.AspNetCore.Builder;

/// <summary>使路线组为持久作业服务:长期执行的长效高端高端低端高端高端高端高端高端高端高端高端高端高端</summary>
/// <remarks>
/// <para>
/// <see cref="WithDurableOperations"/> 给一个组的每个端点,如果该组的处理器返回了一个耐用 grain 调用,例如
/// <c>grain.PlaceOrderAsync(order)</c>或提及现有任务,例如
/// <c>grain.GetDurableTask&lt;T&gt;(id)</c>,启动一个操作来进行操作,绘制观察的终点图;
/// 取消, 并信号该组的操作。 该组的其他端点没有变化 。
/// </para>
/// <para>
/// 操作由 <c>Idempotency-Key</c> 请求的启动时间,以便重新审理的请求到达
/// 将该组的范围、该组的数值和该组的值合并在一起
/// 调用器的范围,例如
/// <see cref="DurableHttpOptions.GetCallerScope"/> 返回,并识别符号。操作主机 grain,该密钥
/// 仅凭识别符号即可收到请求,
/// 仅从其调用范围中设置。 验证和授权属于应用程序, 它在
/// 组的端点, 如 <c>RequireAuthorization</c> 或端点过滤器 。
/// </para>
/// </remarks>
public static class DurableHttpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// 使该组的终点点恢复持久的工作启动作业,并绘制为
    /// 组在 <c>{group}/operations</c>下的业务。
    /// </summary>
    /// <param name="group">路线组,例如 <c>app.MapGroup("/orders/{orderId}")</c>.</param>
    /// <param name="scope">
    /// 集团业务的范围,例如 <c>orders</c>等,是每个作业钥匙的一部分,因此它要留在
    /// 群体和Nexus服务,其范围与路线值相同,处理的都是:
    /// 同样的操作。
    /// </param>
    /// <returns>组,该组绘制了小组耐久启动和信号图,并配置了其终点。</returns>
    /// <remarks>
    /// <para>启动,例如 <c>POST {group}/place</c>,其处理器返回耐久工作,回应:</para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>200 OK</c> ,结果为:或 <c>204 No Content</c> ,当操作成功时;
    /// 包括等待时间在内的 <c>Prefer: wait=N</c> 标题请求。
    /// </description></item>
    /// <item><description>
    /// <c>202 Accepted</c> ,在运行期间有业务状态监测器,包括收到之前的重试
    /// 完整:重试到达运行中的操作,因为 Azure 重复请求和Strake的 v2 应急恢复
    /// 工作,其中IETTF <c>Idempotency-Key</c> 草案返回 <c>409 Conflict</c>。
    /// </description></item>
    /// <item><description>
    /// <c>424 Failed Dependency</c> 失败
    /// 工作 grain 拒绝工作,但没有将工作与 <see cref="DurableOperationError.NotStartedCode"/>一起发生故障
    /// 错误代码,而新密钥又重新开始工作。如果一个操作的工程 grain 没有确认工程失败
    /// <see cref="DurableOperationError.OutcomeUnknownCode"/> 错误代码:工作可能已经运行,因此客户端会检查
    /// 效果在它重新开始工作之前。
    /// </description></item>
    /// <item><description>
    /// <c>400 Bad Request</c> 用于替换无效或缺失的密钥,以及 <c>422 Unprocessable Content</c> 另一关键
    /// 启动其他工作时使用的请求: 另一种方法的呼叫或另一种 grain, 或订阅另一种
    /// 任务。重试其参数不同而进入操作的,因为持久任务运行时间可以确定操作。
    /// </description></item>
    /// <item><description>
    /// <c>503 Service Unavailable</c> ,在请求未达到操作时有 <c>Retry-After</c> 页眉,但该请求没有到达操作,而它
    /// 左转不变, 所以客户端会用相同的密钥再次发送它 。
    /// </description></item>
    /// </list>
    /// <para>
    /// 每项确定行动所在的对应措施都带有 <c>Operation-Location</c> 头及其状况监测器
    /// URL 。 处理器运行每个请求,包括重试,并且只创建工作; 端点开始它。
    /// </para>
    /// <para>组为下列业务提供服务:</para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>GET {group}/operations/{operationId}</c> 返回状态监视器。一个 <c>Prefer: wait=N</c> 头型长号单词:
    /// 等待响应等待操作为终端或等待过折。运行中的操作响应包含
    /// <c>Retry-After</c> 页眉之后。
    /// </description></item>
    /// <item><description>
    /// <c>GET {group}/operations/{operationId}/events</c> 状态监视器的状态流 <c>status</c> 事件事件
    /// 身份识别特征是其状态:现状,然后是运行中的操作的结束状态,随后是
    /// 与该串流结束的溪流连接。 <c>Last-Event-ID</c> 是一个终端状态返回 <c>204 No Content</c>,
    /// 以结束 <c>EventSource</c>的连接。
    /// </description></item>
    /// <item><description>
    /// <c>POST {group}/operations/{operationId}:cancel</c> 可明显地请求取消运行中的操作,并携带
    /// 可选 <c>reason</c> 排 年月a日, JSON 返回 <c>202 Accepted</c> 具有状况监测器;终端
    /// 返回 <c>200 OK</c> ,不变。订阅现有任务的业务将结束其订阅
    /// 并报告说,该合同被取消了,而其他缔约方自己承担的任务仍在继续。
    /// </description></item>
    /// </list>
    /// <para>不存在或保留已经结束的操作返回 <c>404 Not Found</c>。</para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="scope"/> 是空的。</exception>
    public static RouteGroupBuilder WithDurableOperations(this RouteGroupBuilder group, string scope)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentException.ThrowIfNullOrEmpty(scope);
        var metadata = new DurableOperationsMetadata(scope);
        group.WithMetadata(metadata);
        group.AddEndpointFilterFactory(static (context, next) =>
            DurableTaskEndpointConventions.GetResultType(context.MethodInfo) is null ? next : invocation => StartAsync(invocation, next));
        ((IEndpointConventionBuilder)group).Finally(static endpoint => DurableTaskEndpointConventions.DescribeStart(endpoint));

        group.MapGet("/operations/{operationId}", static context => ExecuteAsync(context, Operations(context).GetAsync(context)))
            .WithMetadata(new DurableOperationStatusEndpoint(metadata))
            .WithResponse(StatusCodes.Status200OK, typeof(DurableOperation))
            .WithProblem(StatusCodes.Status404NotFound)
            .WithProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/operations/{operationId}/events", static context => ExecuteAsync(context, Operations(context).WatchAsync(context)))
            .WithResponse(StatusCodes.Status200OK, typeof(DurableOperation), "text/event-stream")
            .WithResponse(StatusCodes.Status204NoContent)
            .WithProblem(StatusCodes.Status404NotFound);

        group.MapPost("/operations/{operationId}:cancel", static context => ExecuteAsync(context, Operations(context).CancelAsync(context)))
            .WithMetadata(new AcceptsMetadata(["application/json"], typeof(DurableOperationCancelRequest), isOptional: true))
            .WithResponse(StatusCodes.Status202Accepted, typeof(DurableOperation))
            .WithResponse(StatusCodes.Status200OK, typeof(DurableOperation))
            .WithProblem(StatusCodes.Status404NotFound);

        return group;
    }

    /// <summary>
    /// 地图 <c>POST {group}/operations/{operationId}/signals/{name}</c>,组内
    /// <see cref="WithDurableOperations"/> 配置,提供 <paramref name="signal"/> 和 JSON 请求机构
    /// 因为它对一个操作工作的价值。
    /// </summary>
    /// <typeparam name="T">信号值类型。</typeparam>
    /// <param name="endpoints">组。</param>
    /// <param name="signal">信号,其名称完成路径。</param>
    /// <returns>配置终点的构建器。</returns>
    /// <remarks>
    /// 端点返回 <c>204 No Content</c> ,一旦工作任务持久地持有价值,包括重复
    /// 具有等值信号, 以相同值的信号, 所以客户端会安全重试一个信号。 没有返回身体的请求
    /// <c>400 Bad Request</c>。与任务持有的值或未完成操作而完成的操作不同
    /// 返回 <c>409 Conflict</c>,未知的操作返回 <c>404 Not Found</c>。
    /// 工作返回之前的操作完成后,主机将记录到工作返回 <c>503 Service Unavailable</c> 具有 a 的
    /// <c>Retry-After</c> 页眉之后。
    /// </remarks>
    /// <exception cref="ArgumentException">信号的名称除 URL - 无保留的字符外,还有其它字符。</exception>
    public static IEndpointConventionBuilder MapDurableSignal<T>(this IEndpointRouteBuilder endpoints, DurableSignal<T> signal)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(signal);
        DurableTaskEndpointConventions.ThrowIfInvalidName(signal.Name, nameof(signal));
        return endpoints.MapPost(
                $"/operations/{{operationId}}/signals/{signal.Name}",
                context => ExecuteAsync(context, Operations(context).SignalAsync(context, signal)))
            .WithMetadata(new AcceptsMetadata(["application/json"], typeof(T)))
            .WithResponse(StatusCodes.Status204NoContent)
            .WithProblem(StatusCodes.Status400BadRequest)
            .WithProblem(StatusCodes.Status404NotFound)
            .WithProblem(StatusCodes.Status409Conflict)
            .WithProblem(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>执行操作端点处理器的 <paramref name="result"/> 。</summary>
    /// <remarks>
    /// 业务的终点是请代表提出请求,他们要求不考虑而约束其请求,因此他们
    /// 修补和 NativeAOT 应用程序。
    /// </remarks>
    internal static async Task ExecuteAsync(HttpContext context, Task<IResult> result) => await (await result).ExecuteAsync(context);

    private static DurableHttpOperations Operations(HttpContext context) => context.RequestServices.GetRequiredService<DurableHttpOperations>();

    internal static TBuilder WithResponse<TBuilder>(this TBuilder builder, int statusCode, Type? type = null, string contentType = "application/json")
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(type is null
            ? new ProducesResponseTypeMetadata(statusCode, typeof(void))
            : new ProducesResponseTypeMetadata(statusCode, type, [contentType]));

    private static TBuilder WithProblem<TBuilder>(this TBuilder builder, int statusCode)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithResponse(statusCode, typeof(ProblemDetails), "application/problem+json");

    /// <summary>启动持久工作,当端点的处理者返回时,该工作是请求方关键名称的操作。</summary>
    private static async ValueTask<object?> StartAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var operations = httpContext.RequestServices.GetRequiredService<DurableHttpOperations>();
        if (!DurableHttpRequestHeaders.TryGetIdempotencyKey(httpContext.Request.Headers, out var key, out var error))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Idempotency-Key is invalid", detail: error);
        }

        if (key is null && operations.Options.RequireIdempotencyKey)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Idempotency-Key is missing",
                detail: "This operation requires an Idempotency-Key header, which identifies the request when it is retried.");
        }

        var result = await next(context);
        if (result is not DurableTask work)
        {
            // 处理者回答说,没有持久工作,例如验证问题。
            return result;
        }

        return await operations.StartAsync(httpContext, DurableOperationsMetadata.Get(httpContext), work, key ?? Guid.NewGuid().ToString("N"));
    }
}
