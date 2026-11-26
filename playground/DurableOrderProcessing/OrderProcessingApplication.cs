using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Core.Internal;
using Orleans.Durable.Tasks;
using Orleans.Durable.Http;
using Orleans.Durable.Journaling;
using Orleans.Durable.Journaling.Json;
using DurableOrderProcessing.Grains;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace DurableOrderProcessing;

// 构成游乐场: silo的持久任务和付款客户,以及为其服务的HTTP API 。
public static class OrderProcessingApplication
{
    // 订单操作的范围,即POPST/api/订单启动的订单操作和Nexus服务 orders.v1 股份,因此订单
    // 游戏场匿名呼叫者的操作键,命令/@/{orderId}通过任一协议都是一样的。
    public const string OrdersScope = "orders";

    // 此信头处理的请求通常会正常处理, 然后重置连接而不是发送回复, 即
    // 服务器处理请求后网络故障。
    public const string SimulateLostResponseHeader = "Simulate-Lost-Response";

    // 配置一个 silo ,用于运行订单工作流程,并配置客户端,他们将客户端称为支付网关。
    public static ISiloBuilder AddOrderProcessing(this ISiloBuilder silo, Action<IHttpClientBuilder> configurePayments)
    {
        silo.UseInMemoryDurableJobs()
            .AddVolatileJournalStorage()
            .UseJsonJournalFormat(DurableOrderProcessingJsonContext.Default)
            .AddDurableTasks();

        // 付款客户的要求,其支付要求中包含来自其步骤的“能力-钥匙”。
        // 键处理器跟随键, 所以它的重写带有相同的键, 即网关的重复。
        var payments = silo.Services.AddDurableHttpClient(OrderGrain.PaymentsClient);
        payments.AddStandardResilienceHandler(options => options.Retry.Delay = TimeSpan.FromMilliseconds(200));
        configurePayments(payments);

        // 提供命令开始以“行动回击”头标注册的 Webhooks。
        silo.Services.Configure<DurableHttpOptions>(ConfigureOperations);
        // 网络锁定接收器是这个程序, 在回溯地址上 。
        silo.Services.AddDurableHttpCallbacks(static options => options.AllowAddress = IPAddress.IsLoopback);
        return silo;
    }

    // 登记HTTP API的服务,包括模拟付款网关。
    public static IServiceCollection AddOrderProcessingApi(this IServiceCollection services)
    {
        services.AddDurableHttp(ConfigureOperations);
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PaymentGateway>();
        return services;
    }

    public static void ConfigureOperations(DurableHttpOptions options)
    {
        // 拒绝其请求的工作流程,如空篮子顺序,说明原因。
        options.CreateError = static exception => exception is InvalidOperationException
            ? new("Rejected", exception.Message)
            : DurableHttpOptions.DefaultCreateError(exception);

        // 只接受此应用程序接收器的 Webhooks 。
        options.AllowCallback = static (_, url) => url.IsLoopback && url.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal);
    }

    public static WebApplication MapOrderProcessing(this WebApplication app)
    {
        app.Use(SimulateLostResponseAsync);

        var api = app.MapGroup("/api");

        api.MapGet("/catalog", async (IGrainFactory grains) =>
            await Task.WhenAll(CatalogData.Items.Select(item => grains.GetGrain<ICatalogItemGrain>(item.Id).GetAsync())));

        api.MapGet("/buyers/{buyerId}/basket", (string buyerId, IGrainFactory grains) =>
            grains.GetGrain<IBasketGrain>(buyerId).GetAsync());

        // 设置行的数量。 绝对数量的 PUT 是无能的, 所以重复或重新尝试更新不需要按键 。
        api.MapPut("/buyers/{buyerId}/basket/{productId:int}", async (string buyerId, int productId, BasketQuantity body, IGrainFactory grains) =>
            CatalogData.Find(productId) is null
                ? Results.NotFound()
                : Results.Ok(await grains.GetGrain<IBasketGrain>(buyerId).SetQuantityAsync(productId, body.Quantity)));

        api.MapGet("/buyers/{buyerId}/orders", (string buyerId, IGrainFactory grains) =>
            grains.GetGrain<IBuyerGrain>(buyerId).GetOrdersAsync());

        // POPST/api/订单检查买方的篮子是其主管单位-钥匙和集团名称的订单
        // /pi/命令/操作/{orderId}:其状况监测器,长长的投票,其运行服务于每个订单。
        // 事件,取消, 和它的发运信号。
        var orders = api.MapGroup("/orders").WithDurableOperations(OrdersScope);
        orders.MapPost("/", (CheckoutRequest request, [FromHeader(Name = DurableHttpHeaders.IdempotencyKey)] Guid orderId, IGrainFactory grains) =>
                grains.GetGrain<IOrderGrain>(orderId).PlaceOrderAsync(ToPlaceOrder(request)))
            .AddEndpointFilter(ValidateCheckout(DurableHttpHeaders.IdempotencyKey));
        orders.MapDurableSignal(OrderGrain.Shipped);

        orders.MapGet("/{orderId:guid}", async (Guid orderId, IGrainFactory grains) =>
            await grains.GetGrain<IOrderGrain>(orderId).GetAsync() is { } order ? Results.Ok(order) : Results.NotFound());

        // 将订单的历史作为服务器发送事件流传:每个承诺的快照,从提交到发布命令
        // 或取消船舶, 或被挂载。 流流等待第一个快照, 这样客户就可以在命令发出后立即查看命令
        // 接受退票。
        orders.MapGet("/{orderId:guid}/events", (Guid orderId, IGrainFactory grains, HttpContext context) =>
            StreamHistoryAsync(context, grains.GetGrain<IOrderGrain>(orderId)));

        // 取消一项观察恢复的指令:下一次启动将重播其尚未完成的任务。
        orders.MapPost("/{orderId:guid}/deactivate", async (Guid orderId, IGrainFactory grains) =>
            await grains.GetGrain<IOrderGrain>(orderId).AsReference<IGrainManagementExtension>().DeactivateOnIdle());

        // 接收命令以“ 操作回击” 头开始注册的 Webhoooks 。
        api.MapPost("/webhooks/orders", (DurableOperation operation, ILoggerFactory loggers) =>
        {
            var orderId = operation.Id.Replace("\r", "\\r").Replace("\n", "\\n");
            var outcome = (operation.Result?.ToString() ?? operation.Error?.ToString())?.Replace("\r", "\\r").Replace("\n", "\\n");
            loggers.CreateLogger("DurableOrderProcessing.Webhooks").LogInformation(
                "Order {OrderId} is {Status}: {Outcome}", orderId, operation.Status, outcome);
            return Results.Ok();
        });

        // 时间工作流程通过 Nexus 服务 orders.v1下订单。 Nexus 请求的ID是该命令的ID,
        // 服务共享订单范围,因此其业务是/命令/命令/操作所服务的订单。
        app.MapGroup("/nexus")
            .MapNexusService("orders.v1", scope: OrdersScope)
            .MapNexusOperation("place-order", (CheckoutRequest input, [FromHeader(Name = NexusRequestIdHeader)] Guid orderId, IGrainFactory grains) =>
                grains.GetGrain<IOrderGrain>(orderId).PlaceOrderAsync(ToPlaceOrder(input)))
            .AddEndpointFilter(ValidateCheckout(NexusRequestIdHeader));

        app.Services.GetRequiredService<PaymentGateway>().Map(app);
        return app;
    }

    private const string NexusRequestIdHeader = "Nexus-Request-Id";

    private static PlaceOrder ToPlaceOrder(CheckoutRequest request) => new(request.BuyerId, request.Address, request.CardNumber[^4..]);

    // 在操作开始之前验证检查退出。 命令名称为 Canic 格式的 ID 的页头
    // 下划一 UUID, 所以每次要求命令都指定相同的操作 。
    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> ValidateCheckout(string header) =>
        (context, next) =>
        {
            if (context.Arguments.OfType<CheckoutRequest>().FirstOrDefault() is not { } request)
            {
                return next(context);
            }

            var errors = new Dictionary<string, string[]>();
            if (context.Arguments.OfType<Guid>().Single() is var orderId
                && context.HttpContext.Request.Headers[header].ToString() != orderId.ToString())
            {
                errors[header] = [$"The {header} of an order is its ID, a lowercase UUID such as '{Guid.Empty}'."];
            }

            if (string.IsNullOrWhiteSpace(request.BuyerId))
            {
                errors["buyerId"] = ["A buyer is required."];
            }

            if (request.Address is null)
            {
                errors["address"] = ["An address is required."];
            }

            if (request.CardNumber is not { Length: >= 12 } || !request.CardNumber.All(char.IsAsciiDigit))
            {
                errors["cardNumber"] = ["A card number has at least 12 digits."];
            }

            return errors.Count == 0 ? next(context) : ValueTask.FromResult<object?>(Results.ValidationProblem(errors));
        };

    private static async Task SimulateLostResponseAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Headers.ContainsKey(SimulateLostResponseHeader))
        {
            await next(context);
            return;
        }

        // 回复是写到废弃的流, 所以服务器不会发送它, 重置会跟着进行 。
        context.Response.Body = Stream.Null;
        await next(context);
        context.Abort();
    }

    private static async Task StreamHistoryAsync(HttpContext context, IOrderGrain order)
    {
        var json = context.RequestServices.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
        var response = context.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        await response.Body.FlushAsync(context.RequestAborted);
        await foreach (var snapshot in WatchOrderAsync(order, context.RequestAborted))
        {
            await response.WriteAsync($"data: {JsonSerializer.Serialize(snapshot, json)}\n\n", context.RequestAborted);
            await response.Body.FlushAsync(context.RequestAborted);
        }
    }

    // 检查命令的启动。 取消中止命令的查点; 下一个激活流
    // 历史从头开始, 所以表跳过它已经提供的快照。
    private static async IAsyncEnumerable<OrderDetails> WatchOrderAsync(IOrderGrain order, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var delivered = 0;
        while (true)
        {
            var position = 0;
            await using var snapshots = order.WatchAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                try
                {
                    if (!await snapshots.MoveNextAsync())
                    {
                        yield break;
                    }
                }
                catch (EnumerationAbortedException)
                {
                    break;
                }

                if (position++ == delivered)
                {
                    delivered++;
                    yield return snapshots.Current;
                }
            }
        }
    }
}

internal sealed record BasketQuantity(int Quantity);
