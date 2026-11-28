using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Http;
using Orleans.Durable.Tasks;
using Xunit;

namespace Orleans.Durable.Http.Tests;

/// <summary>通过 Nexus 协议验证持久化操作，以及它们与路由组自身路由的交互。</summary>
[Collection(DurableHttpCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class NexusOperationTests(DurableHttpFixture fixture)
{
    private const string Operation = "/nexus/orders.v1/approval";

    private HttpClient Http => fixture.Http;

    [Fact]
    public async Task RunningOperationReturnsATokenAndARetryAfterCompletionReturnsTheResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString();
        var input = new NexusApprovalInput(orderId, "widget");

        using var started = await StartAsync(input, requestId, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var info = await ReadJsonAsync(started, cancellationToken);
        Assert.Equal("running", info.GetProperty("state").GetString());
        var token = info.GetProperty("token").GetString();
        Assert.Equal(requestId, token);

        using (var retry = await StartAsync(input, requestId, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
            Assert.Equal(token, (await ReadJsonAsync(retry, cancellationToken)).GetProperty("token").GetString());
        }

        // 该服务作用域的路由组对 Nexus 请求标识符所标识的操作进行寻址。
        using (var signal = await Http.PostAsJsonAsync($"/approvals/operations/{requestId}/signals/approved", "ada", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, signal.StatusCode);
        }

        await WaitForStatusAsync(requestId, "Succeeded", cancellationToken);
        using var completed = await StartAsync(input, requestId, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal("succeeded", Assert.Single(completed.Headers.GetValues("Nexus-Operation-State")));
        Assert.Equal("widget approved by ada", await completed.Content.ReadFromJsonAsync<string>(cancellationToken));
    }

    [Fact]
    public async Task CancelRequestsCancellationAndTheOperationReportsItsFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString();
        var input = new NexusApprovalInput(orderId, "widget");
        using var started = await StartAsync(input, requestId, cancellationToken);
        var token = (await ReadJsonAsync(started, cancellationToken)).GetProperty("token").GetString()!;

        using (var cancel = new HttpRequestMessage(HttpMethod.Post, $"{Operation}/cancel"))
        {
            cancel.Headers.Add("Nexus-Operation-Token", token);
            using var canceled = await Http.SendAsync(cancel, cancellationToken);
            Assert.Equal(HttpStatusCode.Accepted, canceled.StatusCode);
            Assert.Equal(0, canceled.Content.Headers.ContentLength);
        }

        await WaitForStatusAsync(requestId, "Canceled", cancellationToken);

        // 将令牌放在查询字符串中的重复取消请求会被接受，但不会产生任何变更。
        using (var repeated = await Http.PostAsync($"{Operation}/cancel?token={Uri.EscapeDataString(token)}", content: null, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, repeated.StatusCode);
        }

        using var retry = await StartAsync(input, requestId, cancellationToken);
        Assert.Equal(HttpStatusCode.FailedDependency, retry.StatusCode);
        var failure = await ReadJsonAsync(retry, cancellationToken);
        Assert.Equal("nexus.OperationError", failure.GetProperty("metadata").GetProperty("type").GetString());
        Assert.Equal("canceled", failure.GetProperty("details").GetProperty("state").GetString());
        Assert.Equal("Canceled", failure.GetProperty("details").GetProperty("code").GetString());
    }

    [Fact]
    public async Task RequestsWhichTheServiceRefusesReceiveHandlerErrors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString();
        using var started = await StartAsync(new NexusApprovalInput(orderId, "widget"), requestId, cancellationToken);
        var token = (await ReadJsonAsync(started, cancellationToken)).GetProperty("token").GetString()!;

        // 输入不同的重试调用同一 Grain 的同一方法，因此到达同一个操作；使用该请求标识符调用
        // 另一 Grain 则会发生冲突。
        using (var changed = await StartAsync(new NexusApprovalInput(orderId, "gadget"), requestId, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, changed.StatusCode);
            Assert.Equal(token, (await ReadJsonAsync(changed, cancellationToken)).GetProperty("token").GetString());
        }

        using (var conflict = await StartAsync(new NexusApprovalInput(Guid.NewGuid().ToString("N"), "widget"), requestId, cancellationToken))
        {
            await AssertHandlerErrorAsync(conflict, HttpStatusCode.Conflict, "CONFLICT", cancellationToken);
        }

        using (var invalid = await StartAsync(new NexusApprovalInput(orderId, "widget"), "a/b", cancellationToken))
        {
            await AssertHandlerErrorAsync(invalid, HttpStatusCode.BadRequest, "BAD_REQUEST", cancellationToken);
        }

        // 另一用户操作的令牌、未知操作的令牌或无效标识符均无法标识任何操作。
        foreach (var (candidate, user) in new[] { (token, "ada"), ("unknown", null), ("a/b", (string?)null) })
        {
            using var cancel = new HttpRequestMessage(HttpMethod.Post, $"{Operation}/cancel");
            cancel.Headers.Add("Nexus-Operation-Token", candidate);
            if (user is not null)
            {
                cancel.Headers.Add(DurableHttpFixture.TestUserHeader, user);
            }

            using var response = await Http.SendAsync(cancel, cancellationToken);
            await AssertHandlerErrorAsync(response, HttpStatusCode.NotFound, "NOT_FOUND", cancellationToken);
        }

        // 被拒绝的请求未产生任何变更。
        using var status = await Http.GetAsync($"/approvals/operations/{requestId}", cancellationToken);
        Assert.Equal("Running", (await ReadJsonAsync(status, cancellationToken)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task StartWhoseWorkItsGrainRejectsFailsAsNotStarted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString();

        // 订单 Grain 已经在此操作的工作将使用的根标识符下保存其他工作。
        var workRootId = TaskId.CreateRoot(DurableHttpFixture.ApprovalsScope).Child("@").Child(requestId).ToString();
        await fixture.Client.GetGrain<IDurableHttpTestGrain>(orderId).EchoAsync("other").WithId(workRootId).ScheduleAsync(cancellationToken);

        using var response = await StartAsync(new NexusApprovalInput(orderId, "widget"), requestId, cancellationToken);
        Assert.Equal(HttpStatusCode.FailedDependency, response.StatusCode);
        var failure = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal("failed", failure.GetProperty("details").GetProperty("state").GetString());
        Assert.Equal(DurableOperationError.NotStartedCode, failure.GetProperty("details").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("8e03978e-40d5-43e8-bc93-6894a57f9324")]
    [InlineData("order #1? 100%")]
    public void PlainTextTokensAreTheirOperationIdentifiers(string operationId)
    {
        var format = new PlainTextNexusOperationTokenFormat();

        var token = format.Format(operationId);

        Assert.Equal(operationId, token);
        Assert.True(format.TryParse(token, out var parsed));
        Assert.Equal(operationId, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("tab\there")]
    public void TokensWhichAreNotOperationIdentifiersAreRejected(string token)
    {
        Assert.False(new PlainTextNexusOperationTokenFormat().TryParse(token, out var operationId));
        Assert.Null(operationId);
    }
    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("{service}")]
    [InlineData("a b")]
    public void NamesHaveUrlUnreservedCharacters(string name)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddDurableHttp();
        using var app = builder.Build();

        Assert.Throws<ArgumentException>(() => app.MapNexusService(name));
        Assert.Throws<ArgumentException>(() => app.MapNexusService("orders.v1").MapNexusOperation(name, static () => default(global::Orleans.Durable.Tasks.DurableTask)!));
    }

    private Task<HttpResponseMessage> StartAsync(NexusApprovalInput input, string requestId, CancellationToken cancellationToken, string? user = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Operation) { Content = JsonContent.Create(input) };
        request.Headers.Add("Nexus-Request-Id", requestId);
        if (user is not null)
        {
            request.Headers.Add(DurableHttpFixture.TestUserHeader, user);
        }

        return Http.SendAsync(request, cancellationToken);
    }

    private async Task WaitForStatusAsync(string operationId, string expected, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/approvals/operations/{operationId}");
        request.Headers.Add("Prefer", "wait=30");
        using var response = await Http.SendAsync(request, cancellationToken);
        Assert.Equal(expected, (await ReadJsonAsync(response, cancellationToken)).GetProperty("status").GetString());
    }

    private static async Task AssertHandlerErrorAsync(HttpResponseMessage response, HttpStatusCode status, string type, CancellationToken cancellationToken)
    {
        Assert.Equal(status, response.StatusCode);
        var failure = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal("nexus.HandlerError", failure.GetProperty("metadata").GetProperty("type").GetString());
        Assert.Equal(type, failure.GetProperty("details").GetProperty("type").GetString());
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
}
