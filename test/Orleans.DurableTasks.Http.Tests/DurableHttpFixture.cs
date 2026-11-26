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
/// 主持一个运行耐久任务的进程内集集和一个 ASP.NET 核心测试服务器,将它们暴露为 HTTP
/// 操作。 Silos 通过 HTTP 客户端调用测试服务器,该客户端将添加耐久的应急密钥。
/// </summary>
public sealed class DurableHttpFixture : IAsyncLifetime
{
    public const string ApiClientName = "api";
    public const string RawClientName = "raw";
    public const string TestUserHeader = "X-Test-User";
    public const string ForbiddenUser = "mallory";
    public const string ApprovalsScope = "approvals";
    public const string ApprovalRootId = "approval";

    /// <summary>获取 <c>/dropped</c>的事件流。</summary>
    public static ConcurrentDictionary<string, DroppedStream> DroppedStreams { get; } = new();

    /// <summary>获得工作 <c>/jobs</c> API,和有多少次 每人被调查。</summary>
    public static ConcurrentDictionary<string, (string Value, int Polls)> Jobs { get; } = new();

    /// <summary>得到请求 <c>/effects/{id}</c>,按标识符: <c>Idempotency-Key</c> 每项请求。</summary>
    /// <remarks>
    /// 第一次请求识别符号,从所持 <c>held-</c> 等待客户放弃,作为请求
    /// 服务器应用了哪个选项, 但失事回复丢失了, 确实如此 。
    /// </remarks>
    public static ConcurrentDictionary<string, ConcurrentQueue<string>> Effects { get; } = new();

    /// <summary>
    /// 获得工作 <c>/held-jobs</c> API,按数值:每个请求有多少个开始,测试是否释放
    /// 之后它的监视器返回结果
    /// </summary>
    public static ConcurrentDictionary<string, HeldJob> HeldJobs { get; } = new();

    /// <summary>
    /// 保护测试服务器中的回调头头,在 silos中取消对它们的保护,作为主机和 silos的键环
    /// 共享是。
    /// </summary>
    private static readonly IDataProtectionProvider DataProtection = new EphemeralDataProtectionProvider();

    private readonly InProcessTestCluster _cluster;
    private TestServer? _server;
    private WebApplication? _app;

    public DurableHttpFixture()
    {
        // 下 silos 共享一个日记商店, 所以a grain 而在另一端又恢复 silo 恢复了状态
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
            // 测试服务器的操作器没有插座, 所以连接就留给它了 。
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

    /// <summary>获取耐久 HTTP客户端,该客户端 silos 用于调用测试服务器。</summary>
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

        // 验证用户请求姓名,以便测试每个用户的业务范围和授权。
        _app.Use((context, next) =>
        {
            if (context.Request.Headers[TestUserHeader] is [{ } user])
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], authenticationType: "Test"));
            }

            return next(context);
        });
        // 每个顺序的组组开始运行其 grain耐久方法的操作。
        // 包括观察、取消或信号其作业的团体的请求:“软件”不得进入任何订单。
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

        // 同意订单的核准, 另一人开始打电话, 而不是开始工作。
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
        // 接收回调,测试通过回调进行。
        _app.MapPost("/callbacks/{name}", static async (string name, HttpRequest request) =>
            await Callbacks.ReceiveAsync(name, request));

        // Azure 资源管理器样式 API,其 Azure-AsyncOperation 在成功之前监测报告 InProgress 。
        _app.MapPost("/arm/jobs", static (EchoRequest request, HttpResponse response) =>
        {
            var id = Guid.NewGuid().ToString("N");
            Jobs[id] = (request.Value, Polls: 0);
            response.Headers["Azure-AsyncOperation"] = $"/arm/operations/{id}";
            response.Headers.RetryAfter = "0";
            return Results.Accepted();
        });
        // 创建 Azure ,它能解答201个问题。
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

        // 一份 API 报告其所在地的运行中工作,直到该工作第二次民意测验结果返回。
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

        // Nexus通过批准,其请求的识别特征说明标明了“核准”范围的业务,而后者是:
        // /批准小组也提供服务,因此其路线流和信号。
        var nexus = _app.MapGroup("/nexus").MapNexusService("orders.v1", scope: ApprovalsScope);
        nexus.MapNexusOperation("approval", static (NexusApprovalInput input, IGrainFactory grains) =>
            grains.GetGrain<IDurableHttpTestGrain>(input.OrderId).WaitForApprovalAsync(input.Item));
        _app.MapGroup("/approvals").WithDurableOperations(ApprovalsScope).MapDurableSignal(DurableHttpTestGrain.Approved);

        // 运行状态后, 操作事件一旦试验释放, 第一个连接就会下降,
        // 重新连接报告终端状态
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

        // 记录每个请求的主管单位-钥匙,并返回服务器应用的对识别码要求的数量。
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

        // 工作持续到测试释放为止
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

        // 以方法对每项请求的应急关键键进行响应,即按能力关键测试读取的方法。
        _app.MapMethods("/echo-idempotency-key", [HttpMethods.Get, HttpMethods.Post], static (HttpRequest request) => request.Headers[DurableHttpHeaders.IdempotencyKey].ToString());

        await _app.StartAsync();
        _server = _app.GetTestServer();
        Http = _app.GetTestClient();
    }

    /// <summary>获取测试服务器收到的回调。</summary>
    public static CallbackReceiver Callbacks { get; } = new();

    private static void ConfigureOperations(DurableHttpOptions options)
    {
        // 测试 grain的例外描述客户的结果。
        options.CreateError = static exception => exception switch
        {
            OperationCanceledException => new("Canceled", exception.Message),
            InvalidOperationException => new(nameof(InvalidOperationException), exception.Message),
            _ => DurableHttpOptions.DefaultCreateError(exception),
        };

        // 只接受对测试服务器接收器的回调 。
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

/// <summary>符号 Webhooks 带有一个用于测试重新计算值的信号。</summary>
public sealed class TestWebhookSigner : IDurableWebhookSigner
{
    public static string Expected(string webhookId, long timestamp, int bodyLength) => $"v1,test:{webhookId}:{timestamp}:{bodyLength}";

    public string? Sign(DurableCallback callback, string webhookId, long timestamp, ReadOnlySpan<byte> body) =>
        Expected(webhookId, timestamp, body.Length);
}

/// <summary>记录测试服务器收到的回调,让测试按名称等待。</summary>
/// <remarks>
/// 姓名开头的接收器 <c>flaky-</c> 第一次尝试时 <c>503 Service Unavailable</c>, 一个
/// 姓名以 <c>slow-</c> 在发件人超时后第一次尝试回答,名字开头的是
/// <c>moved-</c> 将第一次尝试方向转向另一个接收器;发送者分别重试。
/// </remarks>
public sealed class CallbackReceiver
{
    private readonly ConcurrentDictionary<string, Receiver> _receivers = new();

    /// <summary>返回一个任务,该任务随着第一个调回 <paramref name="name"/> 所接受的填充而完成。</summary>
    /// <remarks>启动回拨前先打开等待。</remarks>
    public Task<ReceivedCallback> ExpectAsync(string name) => _receivers.GetOrAdd(name, static _ => new()).Accepted.Task;

    /// <summary>返回收到多少次尝试 <paramref name="name"/> 。</summary>
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
                // 延后回调客户端的超时; 请求中止结束等待 。
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

/// <summary>事件流 <c>/dropped</c>,其第一个连接会下降一次 <see cref="Drop"/> 完整。</summary>
public sealed class DroppedStream
{
    public readonly TaskCompletionSource Drop = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Connections;

    /// <summary>获取重新连接的 <c>Last-Event-ID</c> 。</summary>
    public string? LastEventId { get; set; }
}

/// <summary>
/// 跟随方向的转向,如 <see cref="HttpClientHandler"/> 和 <see cref="HttpClientHandler.AllowAutoRedirect"/>, 过
/// 测试服务器,它不跟随它们, 所以测试观察回击传送是否禁用它们。
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

/// <summary>一份 <c>/held-jobs</c>。</summary>
public sealed class HeldJob
{
    public readonly TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Starts;
    public int Polls;
}
