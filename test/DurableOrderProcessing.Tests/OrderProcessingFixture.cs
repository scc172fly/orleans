using System.Net.Http.Json;
using System.Text.Json;
using DurableOrderProcessing;
using DurableOrderProcessing.Grains;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Durable.Http;
using Orleans.Hosting;
using Orleans.TestingHost;
using Orleans.TestingHost.Logging;
using Xunit;

namespace DurableOrderProcessing.Tests;

/// <summary>
/// 操场主机:运行订单工作流程的流程中集集和一个为 API服务的测试服务器
/// 和模拟付款网关。 silo 通过支付客户端在测试服务器上调用网关。
/// </summary>
public sealed class OrderProcessingFixture : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly InProcessTestCluster _cluster;
    private WebApplication? _app;
    private TestServer? _server;

    public OrderProcessingFixture() : this(configureSilo: null)
    {
    }

    internal OrderProcessingFixture(Action<ISiloBuilder>? configureSilo, short initialSilosCount = 2)
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount);
        builder.ConfigureClient(client => client.AddDurableTasks());
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddOrderProcessing(payments => payments
                .ConfigureHttpClient(static client => client.BaseAddress = new Uri("http://localhost/gateway/"))
                .ConfigurePrimaryHttpMessageHandler(() => new NetworkHandler(Server.CreateHandler())));

            // 命令立即生效,等待付款直到测试取消为止。
            silo.Services.Configure<OrderingOptions>(static options =>
            {
                options.GracePeriod = TimeSpan.Zero;
                options.PaymentTimeout = TimeSpan.FromMinutes(5);
                options.PaymentPollInterval = TimeSpan.FromMilliseconds(100);
            });
            configureSilo?.Invoke(silo);
        });
        _cluster = builder.Build();
    }

    public HttpClient Http { get; private set; } = null!;

    public IClusterClient Client => _cluster.Client;

    internal InProcessTestCluster Cluster => _cluster;

    public PaymentGateway Gateway => _app!.Services.GetRequiredService<PaymentGateway>();

    public InMemoryLogBuffer WebhookLogs { get; } = new();

    private TestServer Server => _server ?? throw new InvalidOperationException("The test server has not started.");

    public async ValueTask InitializeAsync()
    {
        await _cluster.DeployAsync();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.AddProvider(new InMemoryLoggerProvider(WebhookLogs));
        builder.Logging.AddFilter<InMemoryLoggerProvider>(static (category, _) => category == "DurableOrderProcessing.Webhooks");
        builder.Services.AddSingleton<IGrainFactory>(_cluster.Client);
        builder.Services.AddOrderProcessingApi();
        _app = builder.Build();
        _app.MapOrderProcessing();
        await _app.StartAsync();
        _server = _app.GetTestServer();
        Http = _app.GetTestClient();
    }

    /// <summary>向买方篮子填充每件产品的一个单位。</summary>
    public async Task FillBasketAsync(string buyerId, params int[] productIds)
    {
        foreach (var productId in productIds)
        {
            using var response = await Http.PutAsJsonAsync($"/api/buyers/{buyerId}/basket/{productId}", new { quantity = 1 }, TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>发送买方篮子的支票作为订单 <paramref name="orderId"/> 姓名。</summary>
    public Task<HttpResponseMessage> CheckoutAsync(string buyerId, Guid orderId, string cardNumber = "4111111111111111", bool loseResponse = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(Checkout(buyerId, cardNumber)) };
        request.Headers.Add(DurableHttpHeaders.IdempotencyKey, orderId.ToString());
        if (loseResponse)
        {
            request.Headers.Add(OrderProcessingApplication.SimulateLostResponseHeader, "true");
        }

        return Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static CheckoutRequest Checkout(string buyerId, string cardNumber) =>
        new(buyerId, new Address("15703 NE 61st Ct", "Redmond", "WA", "U.S.", "98052"), cardNumber);

    /// <summary>等待订单历史达到 <paramref name="status"/>,并返回快照。</summary>
    public async Task<OrderDetails> WaitForStatusAsync(Guid orderId, OrderStatus status)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        OrderDetails? last = null;
        try
        {
            await foreach (var snapshot in Client.GetGrain<IOrderGrain>(orderId).WatchAsync(timeout.Token).WithCancellation(timeout.Token))
            {
                last = snapshot;
                if (snapshot.Order.Status == status)
                {
                    return snapshot;
                }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Order {orderId} didn't reach {status} within {Timeout}; its last status is {last?.Order.Status.ToString() ?? "none"}.");
        }

        throw new InvalidOperationException($"Order {orderId} ended as {last?.Order.Status} without reaching {status}.");
    }

    /// <summary>命令的运行长时间直到它到达终点,并返回其状态监测器。</summary>
    public async Task<JsonElement> WaitForOperationAsync(Guid orderId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/operations/{orderId}");
            request.Headers.Add("Prefer", "wait=30");
            using var response = await Http.SendAsync(request, cancellationToken).WaitAsync(Timeout, cancellationToken);
            response.EnsureSuccessStatusCode();
            var operation = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (operation.GetProperty("status").GetString() is "Succeeded" or "Failed" or "Canceled")
            {
                return operation;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Http?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        await _cluster.DisposeAsync();
    }

    /// <summary>
    /// 测试服务器的应用程序中止,而测试服务器与
    /// <see cref="OperationCanceledException"/> 或 <see cref="IOException"/>,作为 <see cref="HttpRequestException"/>,
    /// 当套接字处理器报告重置连接时, 付款客户端的复原力处理器将重新检修它 。
    /// </summary>
    private sealed class NetworkHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                throw new HttpRequestException("The connection was reset.", exception);
            }
        }
    }
}

[CollectionDefinition(Name)]
public sealed class OrderProcessingCollection : ICollectionFixture<OrderProcessingFixture>
{
    public const string Name = "OrderProcessing";
}
