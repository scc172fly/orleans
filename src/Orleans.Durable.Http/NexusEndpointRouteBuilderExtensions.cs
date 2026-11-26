using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Durable.Http;

namespace Microsoft.AspNetCore.Builder;

/// <summary>映射 Nexus 服务,其运行开始耐久的工作并取消它,根据 Nexus HTTP 协议。</summary>
/// <remarks>
/// <para>
/// Nexus 是时间时空跨名称空间操作的协议协议 :
/// <c>POST {endpoint}/{service}/{operation}</c> ,用 <c>POST {endpoint}/{service}/{operation}/cancel</c>取消,
/// 以启动返回的标记标出它。 见
/// <see href="https://github.com/nexus-rpc/api/blob/main/SPEC.md">Nexus HTTP 规格</see>。
/// </para>
/// <para>
/// Nexus业务是持久的业务,例如:
/// <see cref="DurableHttpEndpointRouteBuilderExtensions.WithDurableOperations"/> 配置:调用器
/// <c>Nexus-Request-Id</c> 在服务范围内和打电话者(即:
/// <see cref="DurableHttpOptions.GetCallerScope"/> 返回。一个没有路线参数的相同范围组处理相同的操作,以便可以流
/// 身份状态或发送信号。 验证和授权属于应用程序, 它配置
/// 服务终点上的
/// </para>
/// </remarks>
public static class NexusEndpointRouteBuilderExtensions
{
    // 处理器像一个最小 API 处理器一样将请求捆绑在一起, 最小的API通过反射分析。
    private const string HandlerBindingUnreferencedCode =
        "This API may perform reflection on the supplied delegate and its parameters. These types may be trimmed if not directly referenced.";
    private const string HandlerBindingDynamicCode =
        "This API may perform reflection on the supplied delegate and its parameters. These types may require generated code and aren't compatible with native AOT applications.";

    /// <summary>映射 Nexus 服务 <paramref name="service"/>经营,其业务 <see cref="MapNexusOperation"/> 地图。</summary>
    /// <param name="endpoints">终点路由建设器,其前缀是 Nexus 端点,例如 <c>/nexus</c>。</param>
    /// <param name="service">服务名, 例如 <c>payments.v1</c>中, URL -无保留字符的。</param>
    /// <param name="scope">
    /// (a) 服务业务的范围,或 <see langword="null"/> 用于 <c>nexus/{service}</c>.
    /// </param>
    /// <returns>一组构建器,该组构建器将绘制服务运行图并配置其终点。</returns>
    /// <exception cref="ArgumentException"><paramref name="service"/> 是空的, 或除 URL - 无保留的字符外有其它字符。</exception>
    public static RouteGroupBuilder MapNexusService(this IEndpointRouteBuilder endpoints, string service, string? scope = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        DurableTaskEndpointConventions.ThrowIfInvalidName(service, nameof(service));
        return endpoints.MapGroup($"/{service}").WithMetadata(new NexusServiceMetadata(scope ?? $"nexus/{service}"));
    }

    /// <summary>
    /// Nexus 操作地图 <paramref name="operation"/>,其处理器将耐久工作(例如)恢复到
    /// <c>grain.ChargeAsync(input)</c>,启动请求的起始点开始为操作。
    /// </summary>
    /// <param name="service">返回的组 <see cref="MapNexusService"/> 。</param>
    /// <param name="operation">操作名称,例如 <c>charge</c>中, URL -无保留字符的。</param>
    /// <param name="handler">
    /// 代表将操作的输入(例如从 JSON 请求机构等)和最小参数等其他参数连接到运行的输入
    /// API 处理器,返回一个耐久 grain 调用或提及现有任务,
    /// <c>grain.GetDurableTask&lt;T&gt;(id)</c> 直接或通过 <see cref="Task{TResult}"/> 或
    /// <see cref="ValueTask{TResult}"/>. 它运行于每个启动请求, 包括重试, 并且只创建工作 。
    /// </param>
    /// <returns>组合构建器,该构建器配置操作的启动并取消端点。</returns>
    /// <remarks>
    /// <para>
    /// <c>POST {service}/{operation}</c> 请求的操作 <c>Nexus-Request-Id</c> 姓名,
    /// 或对一项没有请求要求的新操作,并答复:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>200 OK</c> ,结果为 <c>Nexus-Operation-State: succeeded</c> ,例如
    /// 完成后再试一次。
    /// </description></item>
    /// <item><description><c>201 Created</c> 和 A 相 <c>OperationInfo</c>,其代号是操作运行时的地址</description></item>
    /// <item><description>
    /// <c>424 Failed Dependency</c> 和 A 相 <c>OperationError</c> 失败者,其信息
    /// <see cref="DurableHttpOptions.CreateError"/> 描述操作失败或取消时的描述。
    /// 工作 grain 拒绝使用 <see cref="DurableOperationError.NotStartedCode"/> 代码失败的工作,其中的编码
    /// 工作 grain 没有确认工作, 可能已经运行了,
    /// <see cref="DurableOperationError.OutcomeUnknownCode"/> 代码。
    /// </description></item>
    /// <item><description>
    /// 处理器错误 : <c>400</c> <c>BAD_REQUEST</c> 对于无效的请求标识符, <c>409</c> <c>CONFLICT</c> a 的a
    /// 请求指定用于启动其他工作的另一项请求的标识符, <c>503</c> <c>UNAVAILABLE</c> 时
    /// 请求没有达到操作, 没有改变, 所以打电话者重新检查它。
    /// </description></item>
    /// </list>
    /// <para>
    /// <c>POST {service}/{operation}/cancel</c> 可持久地要求取消其业务
    /// <c>Nexus-Operation-Token</c> 标题或 <c>token</c> 查询值名称和返回 <c>202 Accepted</c>,包括
    /// 终端操作,它没有改变。一个表示不命名操作的符号接收 <c>404</c> <c>NOT_FOUND</c>
    /// 处理器错误。 <see cref="INexusOperationTokenFormat"/> 格式示意符。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="operation"/> 是空的, 或除 URL - 无保留的字符外有其它字符, 或 <paramref name="handler"/>
    /// 无法恢复持久的工作
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
