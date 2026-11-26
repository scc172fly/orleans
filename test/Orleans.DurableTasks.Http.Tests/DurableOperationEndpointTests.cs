using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Orleans.Durable.Tasks;
using Orleans.Durable.Http;
using Xunit;

namespace Orleans.Durable.Http.Tests;

/// <summary>开展针对HTTP的持久任务行动:一无所有者启动、状况监测员、长期民意调查、事件、取消和信号。</summary>
[Collection(DurableHttpCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class DurableOperationEndpointTests(DurableHttpFixture fixture)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private HttpClient Http => fixture.Http;

    [Fact]
    public async Task RetriedStartReachesTheOperationWhichTheFirstRequestStarted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = $"/orders/{Guid.NewGuid():N}/echo";
        var key = Guid.NewGuid().ToString();

        using var first = await StartAsync(path, new EchoRequest("hello"), key, wait: 30, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("hello", await first.Content.ReadFromJsonAsync<string>(cancellationToken));
        Assert.Equal("wait=30", Assert.Single(first.Headers.GetValues("Preference-Applied")));
        var location = GetOperationLocation(first);

        // 引用的按结构键字段形式在相同的识别特征下确定同一操作。
        using var retry = await StartAsync(path, new EchoRequest("hello"), $"\"{key}\"", wait: 30, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("hello", await retry.Content.ReadFromJsonAsync<string>(cancellationToken));
        Assert.Equal(location, GetOperationLocation(retry));

        // 行动状态监控其按客户钥匙命名的命令生活
        Assert.Equal($"{path[..^"/echo".Length]}/operations/{key}", location.AbsolutePath);
        var operation = await GetOperationAsync(location, wait: 0, cancellationToken);
        Assert.Equal(new DurableOperation(key, DurableOperationStatus.Succeeded, operation.Result), operation);
        Assert.Equal("hello", operation.Result?.GetString());
        Assert.Null(operation.Error);

        // 重试其争论不同时的参数会执行相同的工作,同一方法对同一种工作的呼唤 grain,作为
        // 持久任务运行时间确定行动,因此它能够进入行动;关键冲突下的其他工作。
        using var changed = await StartAsync(path, new EchoRequest("goodbye"), key, wait: 30, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("hello", await changed.Content.ReadFromJsonAsync<string>(cancellationToken));
        using var conflict = await StartAsync($"{path}-later", new EchoRequest("hello"), key, wait: 30, cancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, conflict.StatusCode);
        Assert.Equal("application/problem+json", conflict.Content.Headers.ContentType?.MediaType);

        using var other = await StartAsync(path, new EchoRequest("goodbye"), Guid.NewGuid().ToString(), wait: 30, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal("goodbye", await other.Content.ReadFromJsonAsync<string>(cancellationToken));
        Assert.NotEqual(location, GetOperationLocation(other));
    }

    [Fact]
    public async Task CallerScopesIsolateOperations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = $"/orders/{Guid.NewGuid():N}/echo";
        var key = Guid.NewGuid().ToString();

        using var ada = await StartAsync(path, new EchoRequest("ada's"), key, wait: 30, cancellationToken, user: "ada");
        Assert.Equal("ada's", await ada.Content.ReadFromJsonAsync<string>(cancellationToken));
        var location = GetOperationLocation(ada);

        // 经认证的用户业务属于用户范围,因此是另一个用户或匿名用户的等同钥匙。
        // 请求, 开始自己的操作, 每一个都在同一地址读取自己的操作 。
        using var grace = await StartAsync(path, new EchoRequest("grace's"), key, wait: 30, cancellationToken, user: "grace");
        Assert.Equal(HttpStatusCode.OK, grace.StatusCode);
        Assert.Equal("grace's", await grace.Content.ReadFromJsonAsync<string>(cancellationToken));
        Assert.Equal(location, GetOperationLocation(grace));
        using var anonymous = await StartAsync(path, new EchoRequest("anonymous"), key, wait: 30, cancellationToken);
        Assert.Equal("anonymous", await anonymous.Content.ReadFromJsonAsync<string>(cancellationToken));

        Assert.Equal("ada's", (await GetOperationAsync(location, wait: 0, cancellationToken, user: "ada")).Result?.GetString());
        Assert.Equal("grace's", (await GetOperationAsync(location, wait: 0, cancellationToken, user: "grace")).Result?.GetString());
        Assert.Equal("anonymous", (await GetOperationAsync(location, wait: 0, cancellationToken)).Result?.GetString());

        using var conflict = await StartAsync($"{path}-later", new EchoRequest("ada's"), key, wait: 30, cancellationToken, user: "ada");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, conflict.StatusCode);
    }

    [Fact]
    public async Task OperationKeysAreNotCapabilitiesOfOtherCallers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = $"/orders/{Guid.NewGuid():N}/approval";
        var key = Guid.NewGuid().ToString();
        using var started = await StartAsync(path, new ApprovalRequest("widget"), key, wait: null, cancellationToken, user: "ada");
        var location = GetOperationLocation(started);

        // 另一用户、匿名电话员和匿名打电话者,其关键内容包含Ada的名字,例如
        // "Ada的手术都达不到"
        foreach (var (user, operation) in new[] { ("grace", location), (null, location), (null, new Uri(location.AbsoluteUri.Replace($"/operations/{key}", $"/operations/ada:{key}", StringComparison.Ordinal))) })
        {
            foreach (var request in new[]
            {
                new HttpRequestMessage(HttpMethod.Get, operation),
                new HttpRequestMessage(HttpMethod.Post, $"{operation}:cancel"),
                new HttpRequestMessage(HttpMethod.Post, $"{operation}/signals/approved") { Content = JsonContent.Create("mallory") },
            })
            {
                using (request)
                {
                    if (user is not null)
                    {
                        request.Headers.Add(DurableHttpFixture.TestUserHeader, user);
                    }

                    using var response = await Http.SendAsync(request, cancellationToken);
                    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                }
            }
        }

        Assert.Equal(HttpStatusCode.NoContent, await SignalAsync(location, "ada", cancellationToken, user: "ada"));
        Assert.Equal("widget approved by ada", (await GetOperationAsync(location, wait: 30, cancellationToken, user: "ada")).Result?.GetString());
    }

    [Fact]
    public async Task GroupEndpointFilterAuthorizesEveryRequestOfItsOperations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = $"/orders/{Guid.NewGuid():N}/approval";
        var key = Guid.NewGuid().ToString();
        using var started = await StartAsync(path, new ApprovalRequest("widget"), key, wait: null, cancellationToken, user: "ada");
        var location = GetOperationLocation(started);

        using var start = await StartAsync(path, new ApprovalRequest("widget"), key, wait: null, cancellationToken, user: DurableHttpFixture.ForbiddenUser);
        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
        foreach (var request in new[]
        {
            new HttpRequestMessage(HttpMethod.Get, location),
            new HttpRequestMessage(HttpMethod.Get, $"{location}/events"),
            new HttpRequestMessage(HttpMethod.Post, $"{location}:cancel"),
            new HttpRequestMessage(HttpMethod.Post, $"{location}/signals/approved") { Content = JsonContent.Create("mallory") },
        })
        {
            using (request)
            {
                request.Headers.Add(DurableHttpFixture.TestUserHeader, DurableHttpFixture.ForbiddenUser);
                using var response = await Http.SendAsync(request, cancellationToken);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }

        // 被拒绝的请求没有改变。
        Assert.Equal(DurableOperationStatus.Running, (await GetOperationAsync(location, wait: 0, cancellationToken, user: "ada")).Status);
    }

    [Fact]
    public async Task OperationWhichSubscribesToAnExistingTaskSignalsAndObservesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderId = Guid.NewGuid().ToString("N");
        var grain = fixture.Client.GetGrain<IDurableHttpTestGrain>(orderId);
        await grain.WaitForApprovalAsync("widget").WithId(DurableHttpFixture.ApprovalRootId).ScheduleAsync(cancellationToken);

        using var started = await StartAsync($"/orders/{orderId}/approval/watch", new { }, Guid.NewGuid().ToString(), wait: null, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var location = GetOperationLocation(started);

        // 行动信号达到它所认同的任务,其结果报告。
        Assert.Equal(HttpStatusCode.NoContent, await SignalAsync(location, "ada", cancellationToken));
        var completed = await GetOperationAsync(location, wait: 30, cancellationToken);
        Assert.Equal("widget approved by ada", completed.Result?.GetString());
        var approval = await grain.GetDurableTask<string>(DurableHttpFixture.ApprovalRootId).GetResponseAsync(cancellationToken);
        Assert.Equal("widget approved by ada", approval.GetResult<string>());
    }

    [Fact]
    public async Task CancellingASubscribingOperationEndsOnlyItsSubscription()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderId = Guid.NewGuid().ToString("N");
        var grain = fixture.Client.GetGrain<IDurableHttpTestGrain>(orderId);
        await grain.WaitForApprovalAsync("widget").WithId(DurableHttpFixture.ApprovalRootId).ScheduleAsync(cancellationToken);
        using var started = await StartAsync($"/orders/{orderId}/approval/watch", new { }, Guid.NewGuid().ToString(), wait: null, cancellationToken);
        var location = GetOperationLocation(started);

        using (var cancel = await Http.PostAsJsonAsync(new Uri($"{location}:cancel"), new DurableOperationCancelRequest("the buyer withdrew"), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        }

        var canceled = await GetOperationAsync(location, wait: 30, cancellationToken);
        Assert.Equal(DurableOperationStatus.Canceled, canceled.Status);

        // 其他各方拥有的核准,继续并用自己的信号完成。
        var approval = grain.GetDurableTask<string>(DurableHttpFixture.ApprovalRootId);
        Assert.Equal(DurableTaskStatus.Pending, await approval.GetStatusAsync(cancellationToken: cancellationToken));
        await grain.SignalDurableTaskAsync(DurableHttpFixture.ApprovalRootId, DurableHttpTestGrain.Approved, "ada", cancellationToken);
        Assert.Equal("widget approved by ada", (await approval.GetResponseAsync(cancellationToken)).GetResult<string>());
    }

    [Fact]
    public async Task EndpointsWhichReturnNoDurableWorkAreUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var response = await Http.GetAsync($"/orders/42/summary", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("order 42", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.False(response.Headers.Contains(DurableHttpHeaders.OperationLocation));
    }

    [Theory]
    [InlineData("/orders/{orderId}/echo")]
    [InlineData("/orders/{orderId}/approval/watch")]
    public void StartEndpointsDescribeTheirOperationResponses(string pattern)
    {
        var endpoint = fixture.Endpoints.OfType<RouteEndpoint>().Single(endpoint => endpoint.RoutePattern.RawText == pattern);
        var produces = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();

        Assert.DoesNotContain(produces, static metadata => metadata.Type is { } type && typeof(DurableTask).IsAssignableFrom(type));
        Assert.Contains(produces, static metadata => metadata is { StatusCode: StatusCodes.Status200OK } && metadata.Type == typeof(string));
        Assert.Contains(produces, static metadata => metadata is { StatusCode: StatusCodes.Status202Accepted } && metadata.Type == typeof(DurableOperation));
        Assert.Contains(produces, static metadata => metadata is { StatusCode: StatusCodes.Status424FailedDependency } && metadata.Type == typeof(DurableOperation));
    }

    [Fact]
    public async Task RunningOperationReportsItsStatusAndCompletesWithTheSignaledValue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = $"/orders/{Guid.NewGuid():N}/approval";
        var key = Guid.NewGuid().ToString();

        using var started = await StartAsync(path, new ApprovalRequest("widget"), key, wait: null, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), started.Headers.RetryAfter?.Delta);
        var location = GetOperationLocation(started);
        var accepted = await started.Content.ReadFromJsonAsync<DurableOperation>(cancellationToken);
        Assert.Equal(new DurableOperation(location.Segments[^1], DurableOperationStatus.Running), accepted);

        using (var status = await Http.GetAsync(location, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(1), status.Headers.RetryAfter?.Delta);
            Assert.Equal(DurableOperationStatus.Running, (await status.Content.ReadFromJsonAsync<DurableOperation>(cancellationToken))!.Status);
        }

        Assert.Equal(HttpStatusCode.NoContent, await SignalAsync(location, "ada", cancellationToken));

        var completed = await GetOperationAsync(location, wait: 30, cancellationToken);
        Assert.Equal(DurableOperationStatus.Succeeded, completed.Status);
        Assert.Equal("widget approved by ada", completed.Result?.GetString());

        // 已完成的操作仍然有信号,因此它的成功和另一个价值冲突都得到重试。
        Assert.Equal(HttpStatusCode.NoContent, await SignalAsync(location, "ada", cancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, await SignalAsync(location, "grace", cancellationToken));

        using var retry = await StartAsync(path, new ApprovalRequest("widget"), key, wait: null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("widget approved by ada", await retry.Content.ReadFromJsonAsync<string>(cancellationToken));
        Assert.False(retry.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task SignalBindsItsValueFromAJsonBody()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var started = await StartAsync($"/orders/{Guid.NewGuid():N}/approval", new ApprovalRequest("widget"), Guid.NewGuid().ToString(), wait: null, cancellationToken);
        var signalUri = new Uri($"{GetOperationLocation(started)}/signals/approved");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, await PostAsync(new StringContent("ada"), cancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(new StringContent("\"ada", Encoding.UTF8, "application/json"), cancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(new StringContent("null", Encoding.UTF8, "application/json"), cancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(content: null, cancellationToken));
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(JsonContent.Create("ada"), cancellationToken));

        var completed = await GetOperationAsync(GetOperationLocation(started), wait: 30, cancellationToken);
        Assert.Equal("widget approved by ada", completed.Result?.GetString());

        async Task<HttpStatusCode> PostAsync(HttpContent? content, CancellationToken cancellationToken)
        {
            using var response = await Http.PostAsync(signalUri, content, cancellationToken);
            return response.StatusCode;
        }
    }

    [Fact]
    public async Task SignalWithoutABodyDoesNotDeliverTheDefaultValue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var started = await StartAsync($"/orders/{Guid.NewGuid():N}/confirmation", new ApprovalRequest("widget"), Guid.NewGuid().ToString(), wait: null, cancellationToken);
        var location = GetOperationLocation(started);
        var signalUri = new Uri($"{location}/signals/confirmed");

        using (var bodyless = await Http.PostAsync(signalUri, content: null, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, bodyless.StatusCode);
        }

        // 操作没有价值, 所以客户端发送的值会完成 。
        Assert.Equal(DurableOperationStatus.Running, (await GetOperationAsync(location, wait: 0, cancellationToken)).Status);
        using (var confirmed = await Http.PostAsJsonAsync(signalUri, true, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        }

        Assert.Equal("widget confirmed: True", (await GetOperationAsync(location, wait: 30, cancellationToken)).Result?.GetString());
    }

    [Fact]
    public async Task ConcurrentOperationsOnOneGrainRunIndependently()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = $"/orders/{Guid.NewGuid():N}/approval";
        using var widget = await StartAsync(path, new ApprovalRequest("widget"), Guid.NewGuid().ToString(), wait: null, cancellationToken);
        using var gadget = await StartAsync(path, new ApprovalRequest("gadget"), Guid.NewGuid().ToString(), wait: null, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, widget.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, gadget.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, await SignalAsync(GetOperationLocation(gadget), "grace", cancellationToken));
        Assert.Equal(HttpStatusCode.NoContent, await SignalAsync(GetOperationLocation(widget), "ada", cancellationToken));

        Assert.Equal("widget approved by ada", (await GetOperationAsync(GetOperationLocation(widget), wait: 30, cancellationToken)).Result?.GetString());
        Assert.Equal("gadget approved by grace", (await GetOperationAsync(GetOperationLocation(gadget), wait: 30, cancellationToken)).Result?.GetString());
    }

    [Fact]
    public async Task RunningOperationAcceptsRepeatedSignalsWithTheValueWhichItHolds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var started = await StartAsync($"/orders/{Guid.NewGuid():N}/shipment", new ApprovalRequest("widget"), Guid.NewGuid().ToString(), wait: null, cancellationToken);
        var location = GetOperationLocation(started);

        Assert.Equal(HttpStatusCode.NoContent, await SignalAsync(location, "ada", cancellationToken));
        Assert.Equal(HttpStatusCode.NoContent, await SignalAsync(location, "ada", cancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, await SignalAsync(location, "grace", cancellationToken));
        Assert.Equal(HttpStatusCode.NoContent, await SignalAsync(location, "parcel post", cancellationToken, "shipped"));

        var completed = await GetOperationAsync(location, wait: 30, cancellationToken);
        Assert.Equal("widget approved by ada, shipped by parcel post", completed.Result?.GetString());
    }

    [Fact]
    public async Task FailedOperationReportsItsError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = $"/orders/{Guid.NewGuid():N}/approval";
        var key = Guid.NewGuid().ToString();
        using var started = await StartAsync(path, new ApprovalRequest("widget"), key, wait: null, cancellationToken);
        var location = GetOperationLocation(started);

        Assert.Equal(HttpStatusCode.NoContent, await SignalAsync(location, "reject", cancellationToken));

        var failed = await GetOperationAsync(location, wait: 30, cancellationToken);
        Assert.Equal(DurableOperationStatus.Failed, failed.Status);
        Assert.Equal(new DurableOperationError(nameof(InvalidOperationException), "widget was rejected."), failed.Error);
        Assert.Null(failed.Result);

        using var retry = await StartAsync(path, new ApprovalRequest("widget"), key, wait: null, cancellationToken);
        Assert.Equal(HttpStatusCode.FailedDependency, retry.StatusCode);
        var replayed = await retry.Content.ReadFromJsonAsync<DurableOperation>(cancellationToken);
        Assert.Equal(failed, replayed);
        Assert.Equal(location, GetOperationLocation(retry));
        Assert.Equal(HttpStatusCode.Conflict, await SignalAsync(location, "ada", cancellationToken));
    }

    [Fact]
    public async Task CancellationCarriesItsReasonToTheTask()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var started = await StartAsync($"/orders/{Guid.NewGuid():N}/approval", new ApprovalRequest("widget"), Guid.NewGuid().ToString(), wait: null, cancellationToken);
        var location = GetOperationLocation(started);
        var cancelUri = new Uri($"{location}:cancel");

        using (var malformed = await Http.PostAsync(cancelUri, new StringContent("{", System.Text.Encoding.UTF8, "application/json"), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        }

        using (var cancel = await Http.PostAsJsonAsync(cancelUri, new DurableOperationCancelRequest("the buyer withdrew"), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
            Assert.Equal(DurableOperationStatus.Running, (await cancel.Content.ReadFromJsonAsync<DurableOperation>(cancellationToken))!.Status);
        }

        var canceled = await GetOperationAsync(location, wait: 30, cancellationToken);
        Assert.Equal(DurableOperationStatus.Canceled, canceled.Status);
        Assert.Equal(new DurableOperationError("Canceled", "widget was canceled: the buyer withdrew"), canceled.Error);

        // 取消终端操作时,无论有无身体,均返回其状态不变。
        using var repeated = await Http.PostAsync(cancelUri, content: null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(canceled, await repeated.Content.ReadFromJsonAsync<DurableOperation>(cancellationToken));
    }

    [Fact]
    public async Task EventsStreamTheStatusUntilTheOperationIsTerminal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var started = await StartAsync($"/orders/{Guid.NewGuid():N}/approval", new ApprovalRequest("widget"), Guid.NewGuid().ToString(), wait: null, cancellationToken);
        var location = GetOperationLocation(started);

        using var response = await Http.GetAsync(new Uri($"{location}/events"), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var events = SseParser.Create(stream, static (_, data) => JsonSerializer.Deserialize<DurableOperation>(data)!)
            .EnumerateAsync(cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(Timeout, cancellationToken));
        Assert.Equal("status", events.Current.EventType);
        Assert.Equal(TimeSpan.FromSeconds(1), events.Current.ReconnectionInterval);
        Assert.Equal("Running", events.Current.EventId);
        Assert.Equal(DurableOperationStatus.Running, events.Current.Data.Status);

        Assert.Equal(HttpStatusCode.NoContent, await SignalAsync(location, "ada", cancellationToken));

        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(Timeout, cancellationToken));
        Assert.Equal("status", events.Current.EventType);
        Assert.Equal("Succeeded", events.Current.EventId);
        Assert.Equal(DurableOperationStatus.Succeeded, events.Current.Data.Status);
        Assert.Equal("widget approved by ada", events.Current.Data.Result?.GetString());
        Assert.False(await events.MoveNextAsync().AsTask().WaitAsync(Timeout, cancellationToken));
    }

    [Fact]
    public async Task EventsOfATerminalOperationEndAfterItsStatusAndReconnectsEnd()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var started = await StartAsync($"/orders/{Guid.NewGuid():N}/echo", new EchoRequest("hello"), Guid.NewGuid().ToString(), wait: 30, cancellationToken);
        var eventsUri = new Uri($"{GetOperationLocation(started)}/events");

        using (var response = await Http.GetAsync(eventsUri, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var items = new List<SseItem<DurableOperation>>();
            await foreach (var item in SseParser.Create(stream, static (_, data) => JsonSerializer.Deserialize<DurableOperation>(data)!).EnumerateAsync(cancellationToken))
            {
                items.Add(item);
            }

            var single = Assert.Single(items);
            Assert.Equal("Succeeded", single.EventId);
            Assert.Equal("hello", single.Data.Result?.GetString());
        }

        // A 音 A EventSource 与最后一个事件的身份识别符重新连接, 204无内容结束其再连接。
        using var reconnect = new HttpRequestMessage(HttpMethod.Get, eventsUri);
        reconnect.Headers.Add("Last-Event-ID", "Succeeded");
        using var reconnected = await Http.SendAsync(reconnect, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, reconnected.StatusCode);
    }

    [Fact]
    public async Task OperationsWhichTheGroupDoesNotHoldAreNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var order = $"/orders/{Guid.NewGuid():N}";
        var key = Guid.NewGuid().ToString();
        using var started = await StartAsync($"{order}/echo", new EchoRequest("hello"), key, wait: 30, cancellationToken);

        // 操作属于其命令, 识别符号比钥匙长, 永远不能命名操作 。
        foreach (var operation in new[] { $"{order}/operations/not-an-operation", $"/orders/{Guid.NewGuid():N}/operations/{key}", $"{order}/operations/{new string('k', 256)}" })
        {
            using var status = await Http.GetAsync(operation, cancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
            using var events = await Http.GetAsync($"{operation}/events", cancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, events.StatusCode);
            using var cancel = await Http.PostAsync($"{operation}:cancel", content: null, cancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
            using var signal = await Http.PostAsJsonAsync($"{operation}/signals/approved", "ada", cancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, signal.StatusCode);
        }
    }

    [Fact]
    public async Task KeysWithReservedUrlCharactersAddressTheirOperation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = $"order #1? 100% {Guid.NewGuid():N}";

        using var started = await StartAsync($"/orders/{Guid.NewGuid():N}/echo", new EchoRequest("hello"), key, wait: 30, cancellationToken);

        var operation = await GetOperationAsync(GetOperationLocation(started), wait: 0, cancellationToken);
        Assert.Equal(key, operation.Id);
        Assert.Equal("hello", operation.Result?.GetString());
    }

    [Fact]
    public async Task StartWhoseWorkItsGrainRejectsReportsThatTheWorkNeverRan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderId = Guid.NewGuid().ToString("N");
        var key = Guid.NewGuid().ToString();

        // 订单 grain 已经根据业务单位的工作将使用的根底,在另一个方法下有另外一种方法的任务,所以
        // 拒绝行动
        var workRootId = TaskId.CreateRoot("orders").Child(orderId).Child("@").Child(key).ToString();
        await fixture.Client.GetGrain<IDurableHttpTestGrain>(orderId).EchoLaterAsync("other").WithId(workRootId).ScheduleAsync(cancellationToken);

        using var response = await StartAsync($"/orders/{orderId}/echo", new EchoRequest("hello"), key, wait: 30, cancellationToken);

        Assert.Equal(HttpStatusCode.FailedDependency, response.StatusCode);
        var operation = (await response.Content.ReadFromJsonAsync<DurableOperation>(cancellationToken))!;
        Assert.Equal(DurableOperationStatus.Failed, operation.Status);
        Assert.Equal(DurableOperationError.NotStartedCode, operation.Error?.Code);
    }

    [Fact]
    public async Task StartWithoutAnIdempotencyKeyIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var response = await Http.PostAsJsonAsync($"/orders/{Guid.NewGuid():N}/echo", new EchoRequest("hello"), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains(DurableHttpHeaders.OperationLocation));
    }

    [Fact]
    public async Task InvalidIdempotencyKeysAreRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = $"/orders/{Guid.NewGuid():N}/echo";
        string[][] invalidKeys = [[new string('k', 256)], ["\"unterminated\\\""], ["first", "second"], ["a/b"], ["."], [".."]];
        foreach (var keys in invalidKeys)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new EchoRequest("hello")) };
            request.Headers.TryAddWithoutValidation(DurableHttpHeaders.IdempotencyKey, keys);
            using var response = await Http.SendAsync(request, cancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    private Task<HttpResponseMessage> StartAsync<T>(string path, T body, string key, int? wait, CancellationToken cancellationToken, string? user = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation(DurableHttpHeaders.IdempotencyKey, key);
        if (user is not null)
        {
            request.Headers.Add(DurableHttpFixture.TestUserHeader, user);
        }
        if (wait is { } seconds)
        {
            request.Headers.Add("Prefer", $"wait={seconds}");
        }

        return Http.SendAsync(request, cancellationToken);
    }

    private async Task<DurableOperation> GetOperationAsync(Uri location, int wait, CancellationToken cancellationToken, string? user = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, location);
        request.Headers.Add("Prefer", $"wait={wait}");
        if (user is not null)
        {
            request.Headers.Add(DurableHttpFixture.TestUserHeader, user);
        }

        using var response = await Http.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DurableOperation>(cancellationToken))!;
    }

    private async Task<HttpStatusCode> SignalAsync(Uri location, string value, CancellationToken cancellationToken, string signal = "approved", string? user = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{location}/signals/{signal}")) { Content = JsonContent.Create(value) };
        if (user is not null)
        {
            request.Headers.Add(DurableHttpFixture.TestUserHeader, user);
        }

        using var response = await Http.SendAsync(request, cancellationToken);
        return response.StatusCode;
    }

    private static Uri GetOperationLocation(HttpResponseMessage response) =>
        new(Assert.Single(response.Headers.GetValues(DurableHttpHeaders.OperationLocation)));
}
