using System.Net;
using System.Net.Http.Json;
using Orleans.Core.Internal;
using Orleans.Durable.Http;
using Orleans.Durable.Tasks;
using Orleans.Runtime;
using Xunit;

namespace Orleans.Durable.Http.Tests;

/// <summary>
/// 将停止的弹射输入服务并调用HTTP操作的 grains :发送请求的任务,主机
/// 操作和运行操作工作的 grain 。 符号是服务器对请求的数
/// 应用了应急关键词和业务成果。
/// </summary>
[Collection(DurableHttpCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class DurableHttpRecoveryTests(DurableHttpFixture fixture)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private HttpClient Http => fixture.Http;

    [Fact]
    public async Task ReplayReadsTheRecordedResponseWithoutSendingTheRequestAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = $"effect-{Guid.NewGuid():N}";
        var grain = fixture.Client.GetGrain<IDurableHttpTestGrain>(Guid.NewGuid().ToString("N"));
        await grain.ApplyEffectAsync(id).WithId("effect").ScheduleAsync(cancellationToken);

        // 此步骤在服务器收到请求后即存在, 因为运行时间在运行前先登记一个步骤
        // 。下一次激活重播任务,其步骤返回它记录的反应。
        await WaitUntilAsync(() => Requests(id).Length == 1, cancellationToken);
        var step = grain.GetDurableTask<DurableHttpResponse>(TaskId.CreateRoot("effect").Child("apply"));
        await step.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        await DeactivateAsync(grain.GetGrainId(), cancellationToken);
        await grain.SignalDurableTaskAsync("effect", DurableHttpTestGrain.Approved, "ada", cancellationToken);

        var result = await grain.GetDurableTask<string>("effect").GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        Assert.Equal("1 ada", result.GetResult<string>());
        Assert.Single(Requests(id));
    }

    [Fact]
    public async Task StepWhichADeactivationInterruptsSendsItsRequestAgainWithTheSameKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = $"held-{Guid.NewGuid():N}";
        var grain = fixture.Client.GetGrain<IDurableHttpTestGrain>(Guid.NewGuid().ToString("N"));
        await grain.ApplyEffectAsync(id).WithId("effect").ScheduleAsync(cancellationToken);

        // 服务器应用了第一个请求, 而中止该步骤的取消会丢失响应, 作为 silo
        // 关闭的会
        await WaitUntilAsync(() => Requests(id).Length == 1, cancellationToken);
        var activation = DurableHttpTestGrain.Activations[grain.GetGrainId()];
        activation.Deactivate(new DeactivationReason(DeactivationReasonCode.ApplicationRequested, "The test interrupts the step."), cancellationToken);
        await activation.Deactivated.WaitAsync(Timeout, cancellationToken);
        await grain.SignalDurableTaskAsync("effect", DurableHttpTestGrain.Approved, "ada", cancellationToken);

        var result = await grain.GetDurableTask<string>("effect").GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        Assert.Equal("2 ada", result.GetResult<string>());
        var keys = Requests(id);
        Assert.Equal(2, keys.Length);
        Assert.False(string.IsNullOrEmpty(keys[0]));
        Assert.Equal(keys[0], keys[1]);
    }

    [Fact]
    public async Task ResumedPollDoesNotStartTheOperationAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var value = Guid.NewGuid().ToString("N");
        var grain = fixture.Client.GetGrain<IDurableHttpTestGrain>(Guid.NewGuid().ToString("N"));
        await grain.RunRemoteOperationAsync("/held-jobs", value, DurableHttpWaitMode.Poll).WithId("job").ScheduleAsync(cancellationToken);

        // 一次民意测验遵循了开始的记录, 下一次启动后又恢复了任务调查。
        await WaitUntilAsync(() => DurableHttpFixture.HeldJobs.TryGetValue(value, out var job) && job.Polls > 0, cancellationToken);
        await DeactivateAsync(grain.GetGrainId(), cancellationToken);
        DurableHttpFixture.HeldJobs[value].Released.SetResult();

        var result = await grain.GetDurableTask<string>("job").GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
        Assert.Equal(value, result.GetResult<string>());
        Assert.Equal(1, DurableHttpFixture.HeldJobs[value].Starts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OperationWhoseHostOrWorkReactivatesReceivesItsSignal(bool deactivateHost)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderId = Guid.NewGuid().ToString("N");
        var (location, key) = await StartApprovalAsync(orderId, cancellationToken);

        await DeactivateAsync(deactivateHost ? HostId(orderId, key) : fixture.Client.GetGrain<IDurableHttpTestGrain>(orderId).GetGrainId(), cancellationToken);
        using (var signal = await Http.PostAsJsonAsync($"{location}/signals/approved", "ada", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, signal.StatusCode);
        }

        var completed = await GetOperationAsync(location, cancellationToken);
        Assert.Equal("widget approved by ada", completed.Result?.GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OperationWhoseHostOrWorkReactivatesIsCanceled(bool deactivateHost)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderId = Guid.NewGuid().ToString("N");
        var (location, key) = await StartApprovalAsync(orderId, cancellationToken);

        await DeactivateAsync(deactivateHost ? HostId(orderId, key) : fixture.Client.GetGrain<IDurableHttpTestGrain>(orderId).GetGrainId(), cancellationToken);
        using (var cancel = await Http.PostAsJsonAsync($"{location}:cancel", new DurableOperationCancelRequest("withdrawn"), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        }

        var canceled = await GetOperationAsync(location, cancellationToken);
        Assert.Equal(DurableOperationStatus.Canceled, canceled.Status);
        Assert.Equal(new DurableOperationError("Canceled", "widget was canceled: withdrawn"), canceled.Error);
    }

    private async Task<(Uri Location, string Key)> StartApprovalAsync(string orderId, CancellationToken cancellationToken)
    {
        var key = Guid.NewGuid().ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/orders/{orderId}/approval") { Content = JsonContent.Create(new ApprovalRequest("widget")) };
        request.Headers.Add(DurableHttpHeaders.IdempotencyKey, key);
        using var response = await Http.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (new Uri(Assert.Single(response.Headers.GetValues(DurableHttpHeaders.OperationLocation))), key);
    }

    private async Task<DurableOperation> GetOperationAsync(Uri location, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, location);
        request.Headers.Add("Prefer", "wait=30");
        using var response = await Http.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DurableOperation>(cancellationToken))!;
    }

    /// <summary>返回 grain 哪个主机是匿名操作的所在地 <paramref name="key"/> 顺序。</summary>
    private static GrainId HostId(string orderId, string key) =>
        GrainId.Create("orleans-durable-operation", TaskId.CreateRoot("orders").Child(orderId).Child("@").Child(key).ToString());

    private ValueTask DeactivateAsync(GrainId grainId, CancellationToken cancellationToken) =>
        fixture.Client.GetGrain(grainId).AsReference<IGrainManagementExtension>().DeactivateOnIdle(cancellationToken);

    private static string[] Requests(string id) => DurableHttpFixture.Effects.TryGetValue(id, out var requests) ? [.. requests] : [];

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }
}
