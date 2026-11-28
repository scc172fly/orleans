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

/// <summary>使路由组提供持久化操作，即由持久化任务执行的长时间运行 HTTP 操作。</summary>
/// <remarks>
/// <para>
/// <see cref="WithDurableOperations"/> 使组中处理程序返回持久化 Grain 调用的每个端点，例如
/// <c>grain.PlaceOrderAsync(order)</c>，或者对现有任务的引用，例如
/// <c>grain.GetDurableTask&lt;T&gt;(id)</c>，用于启动执行该工作的操作，并映射用于观察、
/// 取消及向组内操作发送信号的端点。组内其他端点不受影响。
/// </para>
/// <para>
/// 操作由启动请求中的 <c>Idempotency-Key</c> 命名，因此重试请求会到达
/// 首次请求启动的操作。其键由组作用域、请求中的组
/// 路由参数值（如订单标识符）、调用方作用域（由
/// <see cref="DurableHttpOptions.GetCallerScope"/> 返回）以及操作标识符组成。通过该键寻址的操作宿主 Grain
/// 承载操作，因此观察、取消或发送信号的请求仅凭操作标识符即可到达操作，且
/// 只能在该调用方作用域内访问。身份验证和授权由应用负责，应用可在组内
/// 端点上配置这些机制，例如使用 <c>RequireAuthorization</c> 或端点筛选器。
/// </para>
/// </remarks>
public static class DurableHttpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// 使组内返回持久化工作的端点启动操作，并映射提供
    /// 组内操作服务的端点，路径为 <c>{group}/operations</c>。
    /// </summary>
    /// <param name="group">路由组，例如 <c>app.MapGroup("/orders/{orderId}")</c>。</param>
    /// <param name="scope">
    /// 组内操作的作用域，例如 <c>orders</c>，它是每个操作键的一部分，因此即使组的
    /// 路由发生变化也保持不变。作用域与路由参数值均相同的路由组和 Nexus 服务会访问
    /// 同一组操作。
    /// </param>
    /// <returns>该组，用于映射组内持久化启动端点及信号端点，并配置其端点。</returns>
    /// <remarks>
    /// <para>若启动请求（例如 <c>POST {group}/place</c>）的处理程序返回持久化工作，则响应如下：</para>
    /// <list type="bullet">
    /// <item><description>
    /// 操作成功时，有结果则返回 <c>200 OK</c> 及结果，无结果则返回 <c>204 No Content</c>；
    /// 这包括在 <c>Prefer: wait=N</c> 请求头指定的等待时限内完成的情况。
    /// </description></item>
    /// <item><description>
    /// 操作运行期间返回 <c>202 Accepted</c> 及其状态监视器，包括操作尚未
    /// 完成时到达的重试：重试访问正在运行的操作，类似 Azure 可重复请求与 Stripe v2 幂等机制恢复
    /// 工作，而 IETF <c>Idempotency-Key</c> 草案对此返回 <c>409 Conflict</c>。
    /// </description></item>
    /// <item><description>
    /// 操作失败或被取消时返回 <c>424 Failed Dependency</c> 及其状态监视器。若执行工作的
    /// Grain 在未存储工作时拒绝该工作，则操作以 <see cref="DurableOperationError.NotStartedCode"/>
    /// 错误码失败，可用新键重新启动工作。若执行工作的 Grain 未确认工作，则以
    /// <see cref="DurableOperationError.OutcomeUnknownCode"/> 错误码失败；由于工作可能已经运行，客户端在重新启动工作前应检查其
    /// 实际效果。
    /// </description></item>
    /// <item><description>
    /// 键无效或缺失时返回 <c>400 Bad Request</c>；若另一请求已使用该键启动不同工作，则返回 <c>422 Unprocessable Content</c>：
    /// 例如调用另一个方法、调用另一个 Grain，或订阅另一个
    /// 任务。即使重试的参数不同，仍访问同一操作，符合持久化任务运行时的操作标识规则。
    /// </description></item>
    /// <item><description>
    /// 请求未到达操作时返回 <c>503 Service Unavailable</c> 并携带 <c>Retry-After</c> 响应标头；操作
    /// 未发生变化，因此客户端使用同一个键重新发送请求。
    /// </description></item>
    /// </list>
    /// <para>
    /// 标识操作的每个响应均携带 <c>Operation-Location</c> 响应头，提供状态监视器的
    /// URL。每个请求（包括重试）都会运行处理程序，但处理程序只创建工作，由端点启动工作。
    /// </para>
    /// <para>该组提供以下操作服务：</para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>GET {group}/operations/{operationId}</c> 返回状态监视器。<c>Prefer: wait=N</c> 请求头启用长轮询：
    /// 等到操作进入终态或等待时限到期后才响应。正在运行的操作的响应携带
    /// <c>Retry-After</c> 响应头。
    /// </description></item>
    /// <item><description>
    /// <c>GET {group}/operations/{operationId}/events</c> 以服务器发送的 <c>status</c> 事件流传输状态监视器，
    /// 事件标识符为各自的状态：先发送当前状态，若操作正在运行，则随后发送终态，之后
    /// 流结束。若重连请求的 <c>Last-Event-ID</c> 为终态，则返回 <c>204 No Content</c>，
    /// 使 <c>EventSource</c> 停止重连。
    /// </description></item>
    /// <item><description>
    /// <c>POST {group}/operations/{operationId}:cancel</c> 持久化地请求取消正在运行的操作，携带
    /// JSON 请求体中可选的 <c>reason</c>，并返回 <c>202 Accepted</c> 及其状态监视器；对于已处于终态的
    /// 操作，则返回 <c>200 OK</c> 且操作保持不变。订阅现有任务的操作结束其订阅
    /// 并报告自身已被取消，而由其他方拥有的任务继续运行。
    /// </description></item>
    /// </list>
    /// <para>操作不存在或保留期已结束时返回 <c>404 Not Found</c>。</para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="scope"/> 为空。</exception>
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
    /// 在由 <c>POST {group}/operations/{operationId}/signals/{name}</c> 所指定的路径上映射端点；该组由
    /// <see cref="WithDurableOperations"/> 配置，此端点将 <paramref name="signal"/> 传递给操作的工作，
    /// 并以 JSON 请求体作为信号值。
    /// </summary>
    /// <typeparam name="T">信号的值类型。</typeparam>
    /// <param name="endpoints">路由组。</param>
    /// <param name="signal">信号，其名称构成路由的最后一段。</param>
    /// <returns>用于配置端点的构建器。</returns>
    /// <remarks>
    /// 执行工作的任务持久化保存该值后，端点返回 <c>204 No Content</c>，包括重复发送相同值的
    /// 信号，因此客户端可以安全重试信号。没有请求体时返回
    /// <c>400 Bad Request</c>。若值不同于任务已保存的值，或操作完成时未收到
    /// 该信号，则返回 <c>409 Conflict</c>；未知操作返回 <c>404 Not Found</c>。若请求
    /// 在操作宿主记录工作之前到达操作，则返回 <c>503 Service Unavailable</c>，并携带
    /// <c>Retry-After</c> 响应头。
    /// </remarks>
    /// <exception cref="ArgumentException">信号名称包含 URL 非保留字符之外的字符。</exception>
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

    /// <summary>执行操作端点处理程序的 <paramref name="result"/>。</summary>
    /// <remarks>
    /// 提供操作服务的端点为请求委托，无需反射即可绑定请求，因此
    /// 可服务经过裁剪的应用和 NativeAOT 应用。
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

    /// <summary>启动端点处理程序返回的持久化工作，将其作为请求键命名的操作。</summary>
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
            // 处理程序返回了不含持久化工作的响应，例如验证问题响应。
            return result;
        }

        return await operations.StartAsync(httpContext, DurableOperationsMetadata.Get(httpContext), work, key ?? Guid.NewGuid().ToString("N"));
    }
}
