using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Durable.Tasks;
using Orleans.Durable.Http;
using Orleans.Runtime;
using Xunit;

namespace Orleans.Durable.Http.Tests;

/// <summary>验证持久化任务随 HTTP 请求发送的幂等键。</summary>
[Collection(DurableHttpCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class DurableIdempotencyKeyHandlerTests(DurableHttpFixture fixture)
{
    [Fact]
    public async Task DurableTaskSendsAKeyWhichIdentifiesItsStepWithUnsafeRequests()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = fixture.Client.GetGrain<IDurableHttpTestGrain>(Guid.NewGuid().ToString("N"));

        var scheduled = await grain.SendIdempotencyKeyAsync().WithId("send-key").ScheduleAsync(cancellationToken);
        var result = (await scheduled.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)).GetResult<string[]>();

        Assert.Equal("send-key/send", result[0]);
        var incarnation = Guid.ParseExact(result[3], "N");
        Assert.NotEqual(Guid.Empty, incarnation);
        Assert.Equal(
            DurableIdempotencyKeyHandler.CreateKey(fixture.ServiceId, grain.GetGrainId(), TaskId.Parse(result[0]), incarnation),
            result[1]);

        // GET 按定义具有幂等性，因此不携带幂等键。
        Assert.Equal(string.Empty, result[2]);
    }

    [Fact]
    public void KeysDistinguishServicesGrainsTasksAndRuns()
    {
        var grain = GrainId.Create("durable-http-test", "a");
        var task = TaskId.CreateRoot("root").Child("send");
        var run = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        var key = DurableIdempotencyKeyHandler.CreateKey("service", grain, task, run);

        Assert.Equal(43, key.Length);
        Assert.Equal(key, DurableIdempotencyKeyHandler.CreateKey("service", grain, task, run));
        Assert.NotEqual(key, DurableIdempotencyKeyHandler.CreateKey("other-service", grain, task, run));
        Assert.NotEqual(key, DurableIdempotencyKeyHandler.CreateKey("service", GrainId.Create("durable-http-test", "b"), task, run));
        Assert.NotEqual(key, DurableIdempotencyKeyHandler.CreateKey("service", grain, TaskId.CreateRoot("root").Child("send$2"), run));

        // 删除任务后使用相同任务标识符进行后续运行时，会发送新的键，因此仍保留
        // 先前运行的键的服务器不会以先前运行的响应回复此次请求。
        Assert.NotEqual(key, DurableIdempotencyKeyHandler.CreateKey("service", grain, task, Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7")));
        Assert.Equal("_eSTqCNKOAQzDa_FdwbVJxUFr7n9QDTjB2QwCu-2cIU", key);
    }

    [Fact]
    public async Task RequestsOutsideDurableTasksAndRequestsWithKeysAreUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = Options.Create(new ClusterOptions { ServiceId = fixture.ServiceId });
        using var client = new HttpClient(new DurableIdempotencyKeyHandler(new NoGrainContextAccessor(), options) { InnerHandler = fixture.Server.CreateHandler() })
        {
            BaseAddress = new Uri("http://localhost/"),
        };

        using (var outside = await client.PostAsync("/echo-idempotency-key", content: null, cancellationToken))
        {
            Assert.Equal(string.Empty, await outside.Content.ReadAsStringAsync(cancellationToken));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo-idempotency-key");
        request.Headers.Add(DurableHttpHeaders.IdempotencyKey, "chosen");
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal("chosen", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public void DefaultErrorsDescribeOnlyTheOutcome()
    {
        Assert.Equal(
            new DurableOperationError("Failed", "The operation failed."),
            DurableHttpOptions.DefaultCreateError(new InvalidOperationException("Grain 'order/42' on silo 10.0.0.4 timed out.")));
        Assert.Equal(
            new DurableOperationError("Canceled", "The operation was canceled."),
            DurableHttpOptions.DefaultCreateError(new OperationCanceledException("internal detail")));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("wait=5", 5)]
    [InlineData("respond-async, wait=7", 7)]
    [InlineData("return=minimal; foo=bar, WAIT = 3", 3)]
    [InlineData("wait=\"4\"", 4)]
    [InlineData("wait=600", 60)]
    [InlineData("wait=-1", 0)]
    [InlineData("wait=soon", 0)]
    [InlineData("respond-async", 0)]
    public void PreferWaitIsCappedAndUnparsablePreferencesAreIgnored(string? prefer, int expectedSeconds)
    {
        var headers = new HeaderDictionary();
        if (prefer is not null)
        {
            headers["Prefer"] = prefer;
        }

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), DurableHttpRequestHeaders.GetWait(headers, TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("\"abc\"", "abc")]
    [InlineData("\"a\\\"b\\\\c\"", "a\"b\\c")]
    [InlineData("  8e03978e-40d5-43e8-bc93-6894a57f9324 ", "8e03978e-40d5-43e8-bc93-6894a57f9324")]
    public void IdempotencyKeysAreBareOrStructuredFieldStrings(string header, string expected)
    {
        var headers = new HeaderDictionary { [DurableHttpHeaders.IdempotencyKey] = header };

        Assert.True(DurableHttpRequestHeaders.TryGetIdempotencyKey(headers, out var key, out _));
        Assert.Equal(expected, key);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"a\"b\"")]
    [InlineData("\"a\\b\"")]
    [InlineData("k\u00e9y")]
    public void InvalidIdempotencyKeysAreReported(string header)
    {
        var headers = new HeaderDictionary { [DurableHttpHeaders.IdempotencyKey] = header };

        Assert.False(DurableHttpRequestHeaders.TryGetIdempotencyKey(headers, out var key, out var error));
        Assert.Null(key);
        Assert.NotNull(error);
    }

    /// <summary>表示在所有 Grain 之外运行的代码；持久化任务之外的请求从不访问此代码。</summary>
    private sealed class NoGrainContextAccessor : IGrainContextAccessor
    {
        public IGrainContext GrainContext => throw new InvalidOperationException("Requests outside durable tasks don't identify a grain.");
    }
}
