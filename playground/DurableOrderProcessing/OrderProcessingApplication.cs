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

// 组装此演示：Silo 的持久化任务和支付客户端，以及提供这些功能的 HTTP API。
public static class OrderProcessingApplication
{
    // 订单操作的作用域。POST /api/orders 在该作用域启动操作，Nexus 服务 orders.v1 也共享此作用域，因此订单的
    // 操作键（此演示中匿名调用者的键为 orders/@/{orderId}）在两种协议中相同。
    public const string OrdersScope = "orders";

    // 带有此请求头的请求将正常处理，随后重置连接而不发送响应，以模拟
    // 服务器处理完请求后发生的网络故障。
    public const string SimulateLostResponseHeader = "Simulate-Lost-Response";

    // 配置运行订单工作流的 Silo，以及工作流调用支付网关所使用的客户端。
    public static ISiloBuilder AddOrderProcessing(this ISiloBuilder silo, Action<IHttpClientBuilder> configurePayments)
    {
        silo.UseInMemoryDurableJobs()
            .AddVolatileJournalStorage()
            .UseJsonJournalFormat(DurableOrderProcessingJsonContext.Default)
            .AddDurableTasks();

        // 支付客户端的请求携带从其步骤派生的 Idempotency-Key。弹性处理器
        // 位于幂等键处理器之后，因此其重试携带相同的键，由网关进行去重。
        var payments = silo.Services.AddDurableHttpClient(OrderGrain.PaymentsClient);
        payments.AddStandardResilienceHandler(options => options.Retry.Delay = TimeSpan.FromMilliseconds(200));
        configurePayments(payments);

        // 投递订单启动请求通过 Operation-Callback 请求头注册的 Webhook。
        silo.Services.Configure<DurableHttpOptions>(ConfigureOperations);
        // Webhook 接收方是此应用本身，使用环回地址。
        silo.Services.AddDurableHttpCallbacks(static options => options.AllowAddress = IPAddress.IsLoopback);
        return silo;
    }

    // 注册 HTTP API 的服务，包括模拟支付网关。
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
        // 拒绝请求的工作流（例如购物车为空时下单）会说明拒绝原因。
        options.CreateError = static exception => exception is InvalidOperationException
            ? new("Rejected", exception.Message)
            : DurableHttpOptions.DefaultCreateError(exception);

        // 仅接受发送到此应用接收端点的 Webhook。
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

        // 设置一项明细的数量。PUT 设置绝对数量具有幂等性，因此重复更新或重试更新不需要幂等键。
        api.MapPut("/buyers/{buyerId}/basket/{productId:int}", async (string buyerId, int productId, BasketQuantity body, IGrainFactory grains) =>
            CatalogData.Find(productId) is null
                ? Results.NotFound()
                : Results.Ok(await grains.GetGrain<IBasketGrain>(buyerId).SetQuantityAsync(productId, body.Quantity)));

        api.MapGet("/buyers/{buyerId}/orders", (string buyerId, IGrainFactory grains) =>
            grains.GetGrain<IBuyerGrain>(buyerId).GetOrdersAsync());

        // POST /api/orders 将买家的购物车结算为由 Idempotency-Key 标识的订单，该路由组
        // 在 /api/orders/operations/{orderId} 提供各订单的操作接口：支持长轮询的状态监视器、
        // 事件、取消操作以及 shipped 信号。
        var orders = api.MapGroup("/orders").WithDurableOperations(OrdersScope);
        orders.MapPost("/", (CheckoutRequest request, [FromHeader(Name = DurableHttpHeaders.IdempotencyKey)] Guid orderId, IGrainFactory grains) =>
                grains.GetGrain<IOrderGrain>(orderId).PlaceOrderAsync(ToPlaceOrder(request)))
            .AddEndpointFilter(ValidateCheckout(DurableHttpHeaders.IdempotencyKey));
        orders.MapDurableSignal(OrderGrain.Shipped);

        orders.MapGet("/{orderId:guid}", async (Guid orderId, IGrainFactory grains) =>
            await grains.GetGrain<IOrderGrain>(orderId).GetAsync() is { } order ? Results.Ok(order) : Results.NotFound());

        // 将订单历史记录作为服务器发送事件流式传输：从订单提交到订单
        // 发货或取消期间的每个已提交快照。该流等待第一个快照，因此客户端可以在
        // 结算请求获接受后立即观察订单。
        orders.MapGet("/{orderId:guid}/events", (Guid orderId, IGrainFactory grains, HttpContext context) =>
            StreamHistoryAsync(context, grains.GetGrain<IOrderGrain>(orderId)));

        // 停用订单 Grain，以观察恢复行为：下一次激活时重放待处理任务。
        orders.MapPost("/{orderId:guid}/deactivate", async (Guid orderId, IGrainFactory grains) =>
            await grains.GetGrain<IOrderGrain>(orderId).AsReference<IGrainManagementExtension>().DeactivateOnIdle());

        // 接收订单启动请求通过 Operation-Callback 请求头注册的 Webhook。
        api.MapPost("/webhooks/orders", (DurableOperation operation, ILoggerFactory loggers) =>
        {
            var orderId = operation.Id.Replace("\r", "\\r").Replace("\n", "\\n");
            var outcome = (operation.Result?.ToString() ?? operation.Error?.ToString())?.Replace("\r", "\\r").Replace("\n", "\\n");
            loggers.CreateLogger("DurableOrderProcessing.Webhooks").LogInformation(
                "Order {OrderId} is {Status}: {Outcome}", orderId, operation.Status, outcome);
            return Results.Ok();
        });

        // Temporal 工作流通过 Nexus 服务 orders.v1 下单。Nexus 请求 ID 即订单 ID，且
        // 该服务共享 orders 作用域，因此其操作即 /api/orders/operations 提供的订单操作。
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

    // 在结算操作启动之前验证请求。标识订单的请求头使用规范形式的订单 ID，即
    // 小写 UUID，因此同一订单的每个请求都标识相同的操作。
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

        // 响应写入会丢弃内容的流，因此服务器不会发送该响应，随后重置连接。
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

    // 跨订单的多个激活实例观察订单。停用会中止订单的枚举；下一次激活重新从头流式传输
    // 历史记录，因此观察操作跳过此前已经投递的快照。
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
