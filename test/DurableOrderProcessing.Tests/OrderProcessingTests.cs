using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DurableOrderProcessing.Grains;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Jobs;
using Orleans.Durable.Http;
using Orleans.Runtime;
using Xunit;

namespace DurableOrderProcessing.Tests;

/// <summary>
/// 运动场的端到端的一等能力:每次退场的一等能力键是它的命令身份,所以
/// 退票达到一个订单, 每个支付步骤的衍生钥匙 到达在网关的一个授权。
/// </summary>
[Collection(OrderProcessingCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrderProcessingTests(OrderProcessingFixture fixture)
{
    private HttpClient Http => fixture.Http;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WebhookLogsEscapeLineBreaksInIdentifiersAndOutcomes(bool failed)
    {
        var id = Guid.NewGuid().ToString();
        var operation = new DurableOperation(
            id + "\r\nforged-id",
            failed ? DurableOperationStatus.Failed : DurableOperationStatus.Succeeded,
            failed ? null : JsonSerializer.SerializeToElement("result\r\nforged-result"),
            failed ? new DurableOperationError("code\r\nforged-code", "message\r\nforged-message") : null);

        using var response = await Http.PostAsJsonAsync("/api/webhooks/orders", operation, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(fixture.WebhookLogs.AllEntries, entry => entry.Message.StartsWith($"Order {id}", StringComparison.Ordinal));
        var expected = failed
            ? $"Order {id}\\r\\nforged-id is Failed: DurableOperationError {{ Code = code\\r\\nforged-code, Message = message\\r\\nforged-message }}"
            : $"Order {id}\\r\\nforged-id is Succeeded: result\\r\\nforged-result";
        Assert.Equal(expected, entry.Message);
    }

    [Fact]
    public async Task ConcurrentCheckoutsWithOneKeyPlaceOneOrderAndAuthorizeOnePayment()
    {
        var buyerId = NewBuyer();
        var orderId = Guid.NewGuid();
        await fixture.FillBasketAsync(buyerId, 1);

        // 双击后两次发送退票,第三次重试,全部使用顺序键。
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => fixture.CheckoutAsync(buyerId, orderId)));

        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
                Assert.Equal($"http://localhost/api/orders/operations/{orderId}", Assert.Single(response.Headers.GetValues(DurableHttpHeaders.OperationLocation)));
            }
        }

        await fixture.WaitForStatusAsync(orderId, OrderStatus.Paid);
        Assert.Equal(orderId, Assert.Single(await fixture.Client.GetGrain<IBuyerGrain>(buyerId).GetOrdersAsync()).OrderId);
        Assert.Empty(await fixture.Client.GetGrain<IBasketGrain>(buyerId).GetAsync());
        Assert.Equal(1, Assert.Single(fixture.Gateway.List(), entry => entry.OrderId == orderId).Requests);
    }

    [Fact]
    public async Task CheckoutWhoseResponseIsLostReachesItsOrderWhenItIsRetried()
    {
        var buyerId = NewBuyer();
        var orderId = Guid.NewGuid();
        await fixture.FillBasketAsync(buyerId, 2);

        // 服务器设置顺序, 然后在响应到达之前重新连接连接, 测试服务器
        // 被报告为中止的请求 。
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.CheckoutAsync(buyerId, orderId, loseResponse: true));

        using var retry = await fixture.CheckoutAsync(buyerId, orderId);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        var paid = await fixture.WaitForStatusAsync(orderId, OrderStatus.Paid);
        Assert.Equal(2, Assert.Single(paid.Order.Lines).ProductId);
        Assert.Single(await fixture.Client.GetGrain<IBuyerGrain>(buyerId).GetOrdersAsync());
        Assert.Single(fixture.Gateway.List(), entry => entry.OrderId == orderId);
    }

    [Fact]
    public async Task PaymentRequestWhoseResponseIsLostIsRetriedWithItsKeyAndAuthorizesOnce()
    {
        var buyerId = NewBuyer();
        var orderId = Guid.NewGuid();
        await fixture.FillBasketAsync(buyerId, 3);

        // 网关会丢掉第一个对以0004结束的一张卡片的答复,付款客户方会重新提出申请。
        using var started = await fixture.CheckoutAsync(buyerId, orderId, cardNumber: "4111111111110004");
        await fixture.WaitForStatusAsync(orderId, OrderStatus.Paid);

        var authorization = Assert.Single(fixture.Gateway.List(), entry => entry.OrderId == orderId);
        Assert.Equal(2, authorization.Requests);
        Assert.Equal("Succeeded", authorization.Status);
    }

    [Fact]
    public async Task GatewayDeduplicatesAuthorizationsByIdempotencyKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new PaymentAuthorizationRequest(Guid.NewGuid(), 10m, "1111");
        var key = Guid.NewGuid().ToString();

        using (var unkeyed = await Http.PostAsJsonAsync("/gateway/authorizations", request, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unkeyed.StatusCode);
        }

        using var first = await AuthorizeAsync(request, key);
        using var replayed = await AuthorizeAsync(request, key);
        using var conflict = await AuthorizeAsync(request with { Amount = 20m }, key);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(HttpStatusCode.Accepted, replayed.StatusCode);
        Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotent-Replayed")));
        Assert.Equal(first.Headers.GetValues(DurableHttpHeaders.OperationLocation), replayed.Headers.GetValues(DurableHttpHeaders.OperationLocation));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, conflict.StatusCode);
        Assert.Equal(2, Assert.Single(fixture.Gateway.List(), entry => entry.OrderId == request.OrderId).Requests);

        Task<HttpResponseMessage> AuthorizeAsync(PaymentAuthorizationRequest body, string idempotencyKey)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, "/gateway/authorizations") { Content = JsonContent.Create(body) };
            message.Headers.Add(PaymentGateway.IdempotencyKeyHeader, idempotencyKey);
            return Http.SendAsync(message, cancellationToken);
        }
    }

    [Fact]
    public async Task CancellingAnOrderDuringPaymentVoidsItsAuthorizationAndReleasesItsStock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var buyerId = NewBuyer();
        var orderId = Guid.NewGuid();
        var available = (await fixture.Client.GetGrain<ICatalogItemGrain>(10).GetAsync())!.AvailableStock;
        await fixture.FillBasketAsync(buyerId, 10);

        // 网关从不决定一张于0003年结束的卡片,所以订单等待支付。
        using var started = await fixture.CheckoutAsync(buyerId, orderId, cardNumber: "4111111111110003");
        await fixture.WaitForStatusAsync(orderId, OrderStatus.StockConfirmed);
        using (var cancel = await Http.PostAsJsonAsync($"/api/orders/operations/{orderId}:cancel", new DurableOperationCancelRequest("Cancelled by the buyer."), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        }

        var operation = await fixture.WaitForOperationAsync(orderId);
        Assert.Equal("Succeeded", operation.GetProperty("status").GetString());
        Assert.Equal("Cancelled", operation.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal("Cancelled by the buyer.", operation.GetProperty("result").GetProperty("description").GetString());
        Assert.Equal("Canceled", Assert.Single(fixture.Gateway.List(), entry => entry.OrderId == orderId).Status);
        var item = (await fixture.Client.GetGrain<ICatalogItemGrain>(10).GetAsync())!;
        Assert.Equal(available, item.AvailableStock);
        Assert.Equal(0, item.ReservedStock);
    }

    [Fact]
    public async Task ShippedSignalIsIdempotentAndCompletesTheOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var buyerId = NewBuyer();
        var orderId = Guid.NewGuid();
        await fixture.FillBasketAsync(buyerId, 6);
        using var started = await fixture.CheckoutAsync(buyerId, orderId);
        await fixture.WaitForStatusAsync(orderId, OrderStatus.Paid);

        var shipment = new Shipment(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NoContent, await ShipAsync(shipment));
        Assert.Equal(HttpStatusCode.NoContent, await ShipAsync(shipment));
        Assert.Equal(HttpStatusCode.Conflict, await ShipAsync(new Shipment(Guid.NewGuid())));

        var operation = await fixture.WaitForOperationAsync(orderId);
        Assert.Equal("Shipped", operation.GetProperty("result").GetProperty("status").GetString());

        async Task<HttpStatusCode> ShipAsync(Shipment value)
        {
            using var response = await Http.PostAsJsonAsync($"/api/orders/operations/{orderId}/signals/shipped", value, cancellationToken);
            return response.StatusCode;
        }
    }

    [Fact]
    public async Task OrderReplayedAfterDeactivationsAppendsEachSnapshotOnceAndAuthorizesOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var gracePeriod = TimeSpan.FromMinutes(1);
        var clock = new ReplayClock(gracePeriod);
        // 时钟属于此测试, silo 拥有每次替换激活所使用的挥发性日志。
        await using var replay = new OrderProcessingFixture(silo =>
        {
            silo.Services.AddKeyedSingleton<TimeProvider>(DurableJobTimeProviderNames.DurableJobs, clock);
            silo.Services.Configure<OrderingOptions>(options => options.GracePeriod = gracePeriod);
        }, initialSilosCount: 1);
        await replay.InitializeAsync();
        var buyerId = NewBuyer();
        var orderId = Guid.NewGuid();
        var order = replay.Client.GetGrain<IOrderGrain>(orderId);
        await replay.FillBasketAsync(buyerId, 7);
        using var started = await replay.CheckoutAsync(buyerId, orderId);

        // 在其宽限期内取消顺序, 因此下一次激活从开始就重放工作流程。
        await replay.WaitForStatusAsync(orderId, OrderStatus.Submitted);
        Assert.Empty(replay.Gateway.List());
        await DeactivateOrderAsync();
        Assert.Equal(OrderStatus.Submitted, (await order.GetAsync())!.Order.Status);
        Assert.Empty(replay.Gateway.List());
        clock.Resume();
        await replay.WaitForStatusAsync(orderId, OrderStatus.Paid);

        // 解除已付订单及其操作的宿主的功能,因此,运出的信号通过恢复到达两个地点。
        await DeactivateOrderAsync();
        await replay.Cluster.DeactivateAsync(GrainId.Create("orleans-durable-operation", $"orders/@/{orderId}"));
        using (var ship = await replay.Http.PostAsJsonAsync($"/api/orders/operations/{orderId}/signals/shipped", new Shipment(Guid.NewGuid()), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, ship.StatusCode);
        }

        var operation = await replay.WaitForOperationAsync(orderId);
        Assert.Equal("Shipped", operation.GetProperty("result").GetProperty("status").GetString());

        // 每个过渡都附上了一次快照,网关收到一份授权请求。
        var statuses = new List<OrderStatus>();
        await foreach (var snapshot in order.WatchAsync(cancellationToken))
        {
            statuses.Add(snapshot.Order.Status);
        }

        Assert.Equal([OrderStatus.Submitted, OrderStatus.AwaitingValidation, OrderStatus.StockConfirmed, OrderStatus.Paid, OrderStatus.Shipped], statuses);
        var authorization = Assert.Single(replay.Gateway.List(), entry => entry.OrderId == orderId);
        Assert.Equal(1, authorization.Requests);
        Assert.Equal("Succeeded", authorization.Status);

        async Task DeactivateOrderAsync()
        {
            Assert.True(replay.Cluster.TryGetGrainContext(order.GetGrainId(), out var activation));
            var deactivated = activation.Deactivated;
            using var response = await replay.Http.PostAsync($"/api/orders/{orderId}/deactivate", content: null, cancellationToken);
            Assert.True(response.IsSuccessStatusCode);
            await deactivated.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

    [Fact]
    public async Task PaidOrderShipsAfterACancellationRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var buyerId = NewBuyer();
        var orderId = Guid.NewGuid();
        await fixture.FillBasketAsync(buyerId, 1);
        using var started = await fixture.CheckoutAsync(buyerId, orderId);
        await fixture.WaitForStatusAsync(orderId, OrderStatus.Paid);

        using (var cancel = await Http.PostAsJsonAsync($"/api/orders/operations/{orderId}:cancel", new DurableOperationCancelRequest("Too late."), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        }

        using (var ship = await Http.PostAsJsonAsync($"/api/orders/operations/{orderId}/signals/shipped", new Shipment(Guid.NewGuid()), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, ship.StatusCode);
        }

        var operation = await fixture.WaitForOperationAsync(orderId);
        Assert.Equal("Succeeded", operation.GetProperty("status").GetString());
        Assert.Equal("Shipped", operation.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal("Succeeded", Assert.Single(fixture.Gateway.List(), entry => entry.OrderId == orderId).Status);
    }

    [Fact]
    public async Task PaymentWhichTheGatewayFailsCancelsTheOrderAndReleasesItsStock()
    {
        var buyerId = NewBuyer();
        var orderId = Guid.NewGuid();
        var available = (await fixture.Client.GetGrain<ICatalogItemGrain>(12).GetAsync())!.AvailableStock;
        await fixture.FillBasketAsync(buyerId, 12);

        // 网关回答503服务 在恢复力处理器重新试镜后 万五的卡片结束时没有可用
        using var started = await fixture.CheckoutAsync(buyerId, orderId, cardNumber: "4111111111110005");
        var operation = await fixture.WaitForOperationAsync(orderId);

        Assert.Equal("Cancelled", operation.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal("Payment authorization failed.", operation.GetProperty("result").GetProperty("description").GetString());
        Assert.DoesNotContain(fixture.Gateway.List(), entry => entry.OrderId == orderId);
        var item = (await fixture.Client.GetGrain<ICatalogItemGrain>(12).GetAsync())!;
        Assert.Equal(available, item.AvailableStock);
        Assert.Equal(0, item.ReservedStock);
    }

    [Fact]
    public async Task DeclinedPaymentCancelsTheOrder()
    {
        var buyerId = NewBuyer();
        var orderId = Guid.NewGuid();
        await fixture.FillBasketAsync(buyerId, 11);

        using var started = await fixture.CheckoutAsync(buyerId, orderId, cardNumber: "4111111111110002");
        var operation = await fixture.WaitForOperationAsync(orderId);

        Assert.Equal("Cancelled", operation.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal("The card was declined.", operation.GetProperty("result").GetProperty("description").GetString());
    }

    [Fact]
    public async Task CheckoutOfAnEmptyBasketFailsWithItsReason()
    {
        var orderId = Guid.NewGuid();

        using var response = await fixture.CheckoutAsync(NewBuyer(), orderId);
        var operation = await fixture.WaitForOperationAsync(orderId);

        Assert.Equal("Failed", operation.GetProperty("status").GetString());
        Assert.Equal("Rejected", operation.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("The basket is empty.", operation.GetProperty("error").GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-uuid")]
    [InlineData("UPPER")]
    public async Task CheckoutKeyIsTheOrdersCanonicalId(string? key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(OrderProcessingFixture.Checkout(NewBuyer(), "4111111111111111")) };
        if (key is not null)
        {
            request.Headers.Add(DurableHttpHeaders.IdempotencyKey, key == "UPPER" ? Guid.NewGuid().ToString().ToUpperInvariant() : key);
        }

        using var response = await Http.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NexusRequestAndCheckoutWithTheSameIdReachOneOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var buyerId = NewBuyer();
        var orderId = Guid.NewGuid();
        await fixture.FillBasketAsync(buyerId, 8);

        using var nexus = new HttpRequestMessage(HttpMethod.Post, "/nexus/orders.v1/place-order") { Content = JsonContent.Create(OrderProcessingFixture.Checkout(buyerId, "4111111111111111")) };
        nexus.Headers.Add("Nexus-Request-Id", orderId.ToString());
        using var started = await Http.SendAsync(nexus, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        Assert.Equal(orderId.ToString(), (await started.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("token").GetString());

        using var checkout = await fixture.CheckoutAsync(buyerId, orderId);
        Assert.Equal(HttpStatusCode.Accepted, checkout.StatusCode);
        await fixture.WaitForStatusAsync(orderId, OrderStatus.Paid);
        Assert.Single(await fixture.Client.GetGrain<IBuyerGrain>(buyerId).GetOrdersAsync());
    }

    private static string NewBuyer() => $"buyer-{Guid.NewGuid():N}";

    private sealed class ReplayClock(TimeSpan gracePeriod) : TimeProvider
    {
        private readonly DateTimeOffset _start = TimeProvider.System.GetUtcNow();
        private long _resumedAt;

        public override DateTimeOffset GetUtcNow()
        {
            var resumedAt = Interlocked.Read(ref _resumedAt);
            return resumedAt == 0 ? _start : _start + gracePeriod + GetElapsedTime(resumedAt);
        }

        public void Resume() => Interlocked.Exchange(ref _resumedAt, GetTimestamp());
    }
}
