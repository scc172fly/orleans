using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Durable.Tasks;
using Orleans.Durable.Http;
using Orleans.Hosting;
using Orleans.Durable.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace Orleans.Durable.Http.Tests;

/// <summary>
/// 托管一个运行持久化任务的进程内集群和一个 ASP.NET Core 测试服务器，后者将这些任务公开为 HTTP
/// 操作。Silo 通过添加持久化幂等键的 HTTP 客户端调用该测试服务器。
/// </summary>
public sealed class DurableHttpFixture : IAsyncLifetime
{
    public const string ApiClientName = "api";
    public const string RawClientName = "raw";
    public const string TestUserHeader = "X-Test-User";
    public const string ForbiddenUser = "mallory";
    public const string ApprovalsScope = "approvals";
    public const string ApprovalRootId = "approval";

    /// <summary>获取 <c>/dropped</c> 的事件流。</summary>
    public static ConcurrentDictionary<string, DroppedStream> DroppedStreams { get; } = new();

    /// <summary>获取 <c>/jobs</c> API 的作业及各作业的轮询次数。</summary>
    public static ConcurrentDictionary<string, (string Value, int Polls)> Jobs { get; } = new();

    /// <summary>按标识符获取 <c>/effects/{id}</c> 的请求：每个请求的 <c>Idempotency-Key</c>。</summary>
    /// <remarks>
    /// 标识符以 <c>held-</c> 开头的首次请求一直等待，直到其客户端放弃该请求，模拟服务器
    /// 已应用请求但响应因崩溃丢失的情况。
    /// </remarks>
    public static ConcurrentDictionary<string, ConcurrentQueue<string>> Effects { get; } = new();

    /// <summary>
    /// 按值获取 <c>/held-jobs</c> API 的作业：每个作业由多少个请求启动，以及测试是否已释放
    /// 该作业；释放后，其监视器返回结果。
    /// </summary>
    public static ConcurrentDictionary<string, HeldJob> HeldJobs { get; } = new();

    /// <summary>
    /// 在测试服务器中保护回调请求头，并在 Silo 中解除保护，就像宿主与 Silo
    /// 共享密钥环时一样。
    /// </summary>
    private static readonly IDataProtectionProvider DataProtection = new EphemeralDataProtectionProvider();

    private readonly InProcessTestCluster _cluster;
    private TestServer? _server;
    private WebApplication? _app;

    public DurableHttpFixture()
    {
        // Silo 共享一个日志存储，因此 Grain 在另一 Silo 上重新激活时可以恢复其状态。
        var journals = new VolatileJournalStorageProvider(Options.Create(new JournaledStateManagerOptions()));
        var builder = new InProcessTestClusterBuilder();
        builder.ConfigureClient(clientBuilder => clientBuilder.AddDurableTasks());
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddJournaling();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(journals);
            siloBuilder.Services.AddSingleton<IJournalStorageCatalog>(journals);
            siloBuilder.UseInMemoryDurableJobs();
            siloBuilder.AddDurableTasks();
            siloBuilder.Services.AddDurableHttpClient(ApiClientName)
                .ConfigureHttpClient(static client => client.BaseAddress = new Uri("http://localhost/"))
                .ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler());
            siloBuilder.Services.AddHttpClient(RawClientName, static client => client.BaseAddress = new Uri("http://localhost/"))
                .AddDurableIdempotencyKey()
                .ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler());
            // 测试服务器的处理器不使用套接字，因此将连接处理交给该处理器。
            siloBuilder.Services.AddDurableHttpCallbacks(static options =>
                {
                    options.InitialRetryInterval = TimeSpan.FromMilliseconds(100);
                    options.AllowAddress = null;
                })
                .ConfigurePrimaryHttpMessageHandler(() => new RedirectingHandler(Server.CreateHandler()))
                .ConfigureHttpClient(static client => client.Timeout = TimeSpan.FromMilliseconds(500));
            siloBuilder.Services.Configure<DurableHttpOptions>(ConfigureOperations);
            siloBuilder.Services.AddSingleton<IDurableWebhookSigner, TestWebhookSigner>();
            siloBuilder.Services.AddSingleton(DataProtection);
        });
        _cluster = builder.Build();
    }

    public HttpClient Http { get; private set; } = null!;

    public IClusterClient Client => _cluster.Client;

    /// <summary>获取 Silo 调用测试服务器所用的持久化 HTTP 客户端。</summary>
    public IDurableHttpClient DurableHttp => _cluster.GetSiloServiceProvider().GetRequiredKeyedService<IDurableHttpClient>(ApiClientName);

    public TestServer Server => _server ?? throw new InvalidOperationException("The test server has not started.");

    /// <summary>获取测试服务器的端点。</summary>
    public IReadOnlyList<Microsoft.AspNetCore.Http.Endpoint> Endpoints =>
        _app!.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints;

    public string ServiceId => Client.ServiceProvider.GetRequiredService<IOptions<ClusterOptions>>().Value.ServiceId;

    public async ValueTask InitializeAsync()
    {
        await _cluster.DeployAsync();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IGrainFactory>(_cluster.Client);
        builder.Services.AddDurableHttp(ConfigureOperations);
        builder.Services.AddSingleton(DataProtection);

        _app = builder.Build();

        // 对请求指定的用户进行身份验证，使测试覆盖按用户划分的操作作用域和授权行为。
        _app.Use((context, next) =>
        {
            if (context.Request.Headers[TestUserHeader] is [{ } user])
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], authenticationType: "Test"));
            }

            return next(context);
        });
        // 每个订单的路由组启动运行其 Grain 持久化方法的操作。端点筛选器对路由组的每个
        // 请求进行授权，包括观察、取消操作或向操作发送信号的请求：'mallory' 无权访问任何订单。
        var orders = _app.MapGroup("/orders/{orderId}")
            .WithDurableOperations("orders")
            .AddEndpointFilter(static (context, next) =>
                context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value == ForbiddenUser
                    ? ValueTask.FromResult<object?>(Results.Problem(statusCode: StatusCodes.Status403Forbidden))
                    : next(context));
        orders.MapPost("/echo", static (string orderId, EchoRequest request, IGrainFactory grains) =>
            grains.GetGrain<IDurableHttpTestGrain>(orderId).EchoAsync(request.Value));
        orders.MapPost("/approval", static (string orderId, ApprovalRequest request, IGrainFactory grains) =>
            grains.GetGrain<IDurableHttpTestGrain>(orderId).WaitForApprovalAsync(request.Item));
        orders.MapPost("/echo-later", static (string orderId, EchoRequest request, IGrainFactory grains) =>
            grains.GetGrain<IDurableHttpTestGrain>(orderId).EchoLaterAsync(request.Value));
        orders.MapPost("/complete-later", static (string orderId, EchoRequest request, IGrainFactory grains) =>
            grains.GetGrain<IDurableHttpTestGrain>(orderId).CompleteLaterAsync(request.Value));
        orders.MapPost("/fail-later", static (string orderId, EchoRequest request, IGrainFactory grains) =>
            grains.GetGrain<IDurableHttpTestGrain>(orderId).FailLaterAsync(request.Value));

        // 订阅由另一调用者启动的订单审批，而不是启动新工作。
        orders.MapPost("/approval/watch", static (string orderId, IGrainFactory grains) =>
            grains.GetGrain<IDurableHttpTestGrain>(orderId).GetDurableTask<string>(ApprovalRootId));
        orders.MapGet("/summary", static (string orderId) => $"order {orderId}");
        orders.MapPost("/shipment", static (string orderId, ApprovalRequest request, IGrainFactory grains) =>
            grains.GetGrain<IDurableHttpTestGrain>(orderId).WaitForShipmentAsync(request.Item));
        orders.MapPost("/confirmation", static (string orderId, ApprovalRequest request, IGrainFactory grains) =>
            grains.GetGrain<IDurableHttpTestGrain>(orderId).WaitForConfirmationAsync(request.Item));
        orders.MapDurableSignal(DurableHttpTestGrain.Approved);
        orders.MapDurableSignal(DurableHttpTestGrain.Shipped);
        orders.MapDurableSignal(DurableHttpTestGrain.Confirmed);
        // 接收回调，测试通过 Callbacks 等待这些回调。
        _app.MapPost("/callbacks/{name}", static async (string name, HttpRequest request) =>
            await Callbacks.ReceiveAsync(name, request));

        // Azure Resource Manager 风格的 API，其 Azure-AsyncOperation 监视器在成功前报告 InProgress。
        _app.MapPost("/arm/jobs", static (EchoRequest request, HttpResponse response) =>
        {
            var id = Guid.NewGuid().ToString("N");
            Jobs[id] = (request.Value, Polls: 0);
            response.Headers["Azure-AsyncOperation"] = $"/arm/operations/{id}";
            response.Headers.RetryAfter = "0";
            return Results.Accepted();
        });
        // Azure 创建操作：返回 201 Created，同时返回仍在预配的资源及其状态监视器。
        _app.MapPost("/arm/resources", static (EchoRequest request, HttpResponse response) =>
        {
            var id = Guid.NewGuid().ToString("N");
            Jobs[id] = (request.Value, Polls: 0);
            response.Headers["Azure-AsyncOperation"] = $"/arm/operations/{id}";
            response.Headers.RetryAfter = "0";
            return Results.Created($"/arm/resources/{id}", new { name = id, provisioningState = "Creating" });
        });
        _app.MapGet("/arm/operations/{id}", static (string id) =>
        {
            var (value, polls) = Jobs[id];
            Jobs[id] = (value, polls + 1);
            return polls == 0
                ? Results.Json(new { name = id, status = "InProgress" })
                : Results.Json(new { name = id, status = "succeeded", result = value });
        });

        // API 在 Location 指定的位置报告作业正在运行，直至作业的第二次轮询返回结果。
        _app.MapPost("/jobs", static (EchoRequest request) =>
        {
            var id = Guid.NewGuid().ToString("N");
            Jobs[id] = (request.Value, Polls: 0);
            return Results.Accepted($"/jobs/{id}");
        });
        _app.MapGet("/jobs/{id}", static (string id, HttpResponse response) =>
        {
            var (value, polls) = Jobs[id];
            Jobs[id] = (value, polls + 1);
            if (polls == 0)
            {
                response.Headers.RetryAfter = "0";
                return Results.Accepted($"/jobs/{id}");
            }

            return Results.Ok(value);
        });

        // 通过 Nexus 进行审批，其请求标识符标识 'approvals' 作用域的操作，该作用域也由
        // '/approvals' 路由组提供，因此其路由可以流式传输这些操作的事件并向它们发送信号。
        var nexus = _app.MapGroup("/nexus").MapNexusService("orders.v1", scope: ApprovalsScope);
        nexus.MapNexusOperation("approval", static (NexusApprovalInput input, IGrainFactory grains) =>
            grains.GetGrain<IDurableHttpTestGrain>(input.OrderId).WaitForApprovalAsync(input.Item));
        _app.MapGroup("/approvals").WithDurableOperations(ApprovalsScope).MapDurableSignal(DurableHttpTestGrain.Approved);

        // 某个操作的事件：首次连接在报告运行中状态之后，由测试释放时断开；
        // 重新连接时报告终态。
        _app.MapGet("/dropped/{id}/events", static async (string id, HttpContext context) =>
        {
            var stream = DroppedStreams.GetOrAdd(id, static _ => new());
            context.Response.ContentType = "text/event-stream";
            if (Interlocked.Increment(ref stream.Connections) == 1)
            {
                await context.Response.WriteAsync($"event: status\nid: Running\nretry: 10\ndata: {{\"id\":\"{id}\",\"status\":\"Running\"}}\n\n");
                await context.Response.Body.FlushAsync();
                await stream.Drop.Task.WaitAsync(context.RequestAborted);
                context.Abort();
                return;
            }

            stream.LastEventId = context.Request.Headers["Last-Event-ID"].ToString();
            await context.Response.WriteAsync($"event: status\nid: Succeeded\ndata: {{\"id\":\"{id}\",\"status\":\"Succeeded\",\"result\":\"done\"}}\n\n");
        });

        // 记录每个请求的 Idempotency-Key，并返回服务器针对其标识符已应用的请求数量。
        _app.MapPost("/effects/{id}", static async (string id, HttpContext context) =>
        {
            var requests = Effects.GetOrAdd(id, static _ => new());
            requests.Enqueue(context.Request.Headers[DurableHttpHeaders.IdempotencyKey].ToString());
            if (requests.Count == 1 && id.StartsWith("held-", StringComparison.Ordinal))
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, context.RequestAborted).ContinueWith(static _ => { });
                return Results.Empty;
            }

            return Results.Ok(requests.Count);
        });

        // 运行直至测试释放的作业。
        _app.MapPost("/held-jobs", static (EchoRequest request) =>
        {
            var job = HeldJobs.GetOrAdd(request.Value, static _ => new());
            Interlocked.Increment(ref job.Starts);
            return Results.Accepted($"/held-jobs/{request.Value}");
        });
        _app.MapGet("/held-jobs/{value}", static (string value) =>
        {
            var job = HeldJobs[value];
            Interlocked.Increment(ref job.Polls);
            return job.Released.Task.IsCompleted ? Results.Ok(value) : Results.Accepted($"/held-jobs/{value}");
        });

        // 按请求方法回显各请求的幂等键，供幂等键测试读取。
        _app.MapMethods("/echo-idempotency-key", [HttpMethods.Get, HttpMethods.Post], static (HttpRequest request) => request.Headers[DurableHttpHeaders.IdempotencyKey].ToString());

        await _app.StartAsync();
        _server = _app.GetTestServer();
        Http = _app.GetTestClient();
    }

    /// <summary>获取测试服务器接收到的回调。</summary>
    public static CallbackReceiver Callbacks { get; } = new();

    private static void ConfigureOperations(DurableHttpOptions options)
    {
        // 测试 Grain 的异常向客户端描述操作结果。
        options.CreateError = static exception => exception switch
        {
            OperationCanceledException => new("Canceled", exception.Message),
            InvalidOperationException => new(nameof(InvalidOperationException), exception.Message),
            _ => DurableHttpOptions.DefaultCreateError(exception),
        };

        // 只接受发送到测试服务器接收端点的回调。
        options.AllowCallback = static (_, url) => url.Host == "localhost" && url.AbsolutePath.StartsWith("/callbacks/", StringComparison.Ordinal);
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
}

[CollectionDefinition(Name)]
public sealed class DurableHttpCollection : ICollectionFixture<DurableHttpFixture>
{
    public const string Name = "DurableHttp";
}

/// <summary>使用测试会重新计算的值对 Webhook 签名。</summary>
public sealed class TestWebhookSigner : IDurableWebhookSigner
{
    public static string Expected(string webhookId, long timestamp, int bodyLength) => $"v1,test:{webhookId}:{timestamp}:{bodyLength}";

    public string? Sign(DurableCallback callback, string webhookId, long timestamp, ReadOnlySpan<byte> body) =>
        Expected(webhookId, timestamp, body.Length);
}

/// <summary>记录测试服务器接收到的回调，并允许测试按名称等待回调。</summary>
/// <remarks>
/// 名称以 <c>flaky-</c> 开头的接收端首次返回 <c>503 Service Unavailable</c>；
/// 名称以 <c>slow-</c> 开头的接收端在发送方超时后才响应首次尝试；名称以
/// <c>moved-</c> 开头的接收端将首次尝试重定向至其他接收端；发送方会分别重试这些请求。
/// </remarks>
public sealed class CallbackReceiver
{
    private readonly ConcurrentDictionary<string, Receiver> _receivers = new();

    /// <summary>返回一个任务，在 <paramref name="name"/> 接受首个回调时以该回调作为结果完成。</summary>
    /// <remarks>在触发回调之前启动等待。</remarks>
    public Task<ReceivedCallback> ExpectAsync(string name) => _receivers.GetOrAdd(name, static _ => new()).Accepted.Task;

    /// <summary>返回 <paramref name="name"/> 已收到的执行尝试次数。</summary>
    public int GetAttempts(string name) => _receivers.TryGetValue(name, out var receiver) ? receiver.Attempts : 0;

    internal async Task<IResult> ReceiveAsync(string name, HttpRequest request)
    {
        var receiver = _receivers.GetOrAdd(name, static _ => new());
        if (Interlocked.Increment(ref receiver.Attempts) == 1)
        {
            if (name.StartsWith("flaky-", StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (name.StartsWith("slow-", StringComparison.Ordinal))
            {
                // 等待时长超过回调客户端的超时时间；请求中止会结束等待。
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, request.HttpContext.RequestAborted).ContinueWith(static _ => { });
                return Results.Empty;
            }

            if (name.StartsWith("moved-", StringComparison.Ordinal))
            {
                return Results.Redirect($"/callbacks/redirected-{name}", permanent: false, preserveMethod: true);
            }
        }

        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        var headers = request.Headers.ToDictionary(static header => header.Key, static header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        receiver.Accepted.TrySetResult(new(headers, body));
        return Results.Ok();
    }

    private sealed class Receiver
    {
        public readonly TaskCompletionSource<ReceivedCallback> Accepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Attempts;
    }
}

public sealed record ReceivedCallback(IReadOnlyDictionary<string, string> Headers, string Body);

/// <summary>属于 <c>/dropped</c> 的事件流，在 <see cref="Drop"/> 完成时断开首次连接。</summary>
public sealed class DroppedStream
{
    public readonly TaskCompletionSource Drop = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Connections;

    /// <summary>获取重新连接时的 <c>Last-Event-ID</c>。</summary>
    public string? LastEventId { get; set; }
}

/// <summary>
/// 模拟具有 <see cref="HttpClientHandler"/> 及 <see cref="HttpClientHandler.AllowAutoRedirect"/> 重定向行为的处理器，通过
/// 本身不跟随重定向的测试服务器发送请求，使测试能够观察回调投递是否禁用了重定向。
/// </summary>
public sealed class RedirectingHandler(HttpMessageHandler inner) : HttpClientHandler
{
    private readonly HttpMessageInvoker _inner = new(inner);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await _inner.SendAsync(request, cancellationToken);
        if (AllowAutoRedirect && response.Headers.Location is { } location && (int)response.StatusCode is 307 or 308)
        {
            using var redirected = new HttpRequestMessage(request.Method, new Uri(request.RequestUri!, location)) { Content = request.Content };
            response.Dispose();
            return await _inner.SendAsync(redirected, cancellationToken);
        }

        return response;
    }
}

/// <summary>属于 <c>/held-jobs</c> 的作业。</summary>
public sealed class HeldJob
{
    public readonly TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Starts;
    public int Polls;
}
