using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Durable.Http;
using Orleans.Durable.Journaling;
using Orleans.Runtime;

namespace Orleans.Durable.Http.Tests;

public interface IDurableHttpTestGrain : IGrainWithStringKey
{
    DurableTask<string> EchoAsync(string value);

    DurableTask<string> WaitForApprovalAsync(string item);

    DurableTask<string> WaitForShipmentAsync(string item);

    DurableTask<string> WaitForConfirmationAsync(string item);

    DurableTask<string[]> SendIdempotencyKeyAsync();

    DurableTask<string> EchoLaterAsync(string value);

    DurableTask<string> FailLaterAsync(string value);

    DurableTask CompleteLaterAsync(string value);

    DurableTask<string> RunRemoteOperationAsync(string path, string value, DurableHttpWaitMode mode);

    DurableTask<string> ApproveRemoteOperationAsync(string orderId, string approver);

    DurableTask<string> CancelRemoteOperationAsync(string orderId, string reason);

    DurableTask<string[]> SendRemoteRequestsAsync();

    DurableTask<string> ApplyEffectAsync(string id);
}

/// <summary>运行测试中 HTTP 端点开始的耐久任务。</summary>
[GrainType("durable-http-test")]
public sealed class DurableHttpTestGrain(
    IHttpClientFactory httpClients,
    [FromKeyedServices(DurableHttpFixture.ApiClientName)] IDurableHttpClient api) : DurableGrain, IDurableHttpTestGrain
{
    public static readonly DurableSignal<string> Approved = new("approved");
    public static readonly DurableSignal<string> Shipped = new("shipped");
    public static readonly DurableSignal<bool> Confirmed = new("confirmed");

    /// <summary>可以通过身份获取 grain当前启动的状态,该状态在运行一步时测试停止作用。</summary>
    /// <remarks>
    /// 一步运行为非互不连接请求,因此,一个 grain 调用的请求等待其取消。
    /// 直接激活,作为关闭的 silo ,将中止该步骤。
    /// </remarks>
    public static System.Collections.Concurrent.ConcurrentDictionary<GrainId, IGrainContext> Activations { get; } = new();

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Activations[GrainContext.GrainId] = GrainContext;
        return base.OnActivateAsync(cancellationToken);
    }

    public DurableTask<string> EchoAsync(string value) => DurableTask.FromResult(value);

    /// <summary>等待批准者, 批准者失败时失败 <c>reject</c>.</summary>
    public async DurableTask<string> WaitForApprovalAsync(string item)
    {
        string approver;
        try
        {
            approver = await Approved.WaitAsync();
        }
        catch (OperationCanceledException) when (DurableTask.Current.TaskCancellationReason is { } reason)
        {
            throw new OperationCanceledException($"{item} was canceled: {reason}");
        }

        return approver == "reject" ? throw new InvalidOperationException($"{item} was rejected.") : $"{item} approved by {approver}";
    }

    /// <summary>等待批准者,然后等待承运人。</summary>
    public async DurableTask<string> WaitForShipmentAsync(string item)
    {
        var approver = await Approved.WaitAsync();
        var carrier = await Shipped.WaitAsync();
        return $"{item} approved by {approver}, shipped by {carrier}";
    }

    /// <summary>等待确认,其价值为 <see cref="bool"/>。</summary>
    public async DurableTask<string> WaitForConfirmationAsync(string item) => $"{item} confirmed: {await Confirmed.WaitAsync()}";

    /// <summary>返回 <paramref name="value"/> 长期拖延之后,HTTP客户会观察运行中的操作。</summary>
    public async DurableTask<string> EchoLaterAsync(string value)
    {
        await DurableTask.Delay(TimeSpan.FromMilliseconds(100));
        return value;
    }

    /// <summary>拖延时间过后完成,无结果。</summary>
    public async DurableTask CompleteLaterAsync(string value) => await DurableTask.Delay(TimeSpan.FromMilliseconds(100));

    /// <summary>长期拖延之后失败。</summary>
    public async DurableTask<string> FailLaterAsync(string value)
    {
        await DurableTask.Delay(TimeSpan.FromMilliseconds(100));
        throw new InvalidOperationException($"{value} failed.");
    }

    /// <summary>启动在 <paramref name="path"/> 的操作,并可持久地等待结果。</summary>
    public DurableTask<string> RunRemoteOperationAsync(string path, string value, DurableHttpWaitMode mode) =>
        api.RunOperationAsync<string>(
            () => new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new EchoRequest(value)) },
            new DurableHttpOperationOptions { Mode = mode, PollInterval = TimeSpan.FromMilliseconds(100) });

    /// <summary>
    /// 启动对订单的批准,读取运行状态,以 <paramref name="approver"/>批准,以及
    /// 返回结果。
    /// </summary>
    public async DurableTask<string> ApproveRemoteOperationAsync(string orderId, string approver)
    {
        var operation = await StartApprovalAsync(orderId);
        var status = await api.GetOperationAsync(operation.Location!);
        await api.SignalOperationAsync(operation.Location!, Approved.Name, approver);
        var result = await api.WaitForOperationAsync<string>(operation, new() { Mode = DurableHttpWaitMode.Events });
        return $"{status.Status}: {result}";
    }

    /// <summary>
    /// 启动对订单的批准,以 <paramref name="reason"/>取消订单,并返回其错误
    /// 等待失败 。
    /// </summary>
    public async DurableTask<string> CancelRemoteOperationAsync(string orderId, string reason)
    {
        var operation = await StartApprovalAsync(orderId);
        var cancel = await api.CancelOperationAsync(operation.Location!, reason);
        try
        {
            await api.WaitForOperationAsync<string>(operation, new() { PollInterval = TimeSpan.FromMilliseconds(100) });
            return "completed";
        }
        catch (DurableHttpOperationException exception)
        {
            return $"{cancel.Status}: {exception.Status} {exception.Code} {exception.Message}";
        }
    }

    /// <summary>
    /// 发送a <c>POST</c> 和 a 以及 <c>GET</c> 请求中 <see cref="IDurableHttpClient.SendAsync"/>返回
    /// 服务器收到的有效密钥 。
    /// </summary>
    public async DurableTask<string[]> SendRemoteRequestsAsync()
    {
        var post = await api.SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "/echo-idempotency-key")).WithId("post");
        var get = await api.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "/echo-idempotency-key")).WithId("get");
        return [post.EnsureSuccessStatusCode().ReadAsString(), get.EnsureSuccessStatusCode().ReadAsString()];
    }

    /// <summary>
    /// 应用效果 <paramref name="id"/> 服务器上的 <see cref="IDurableHttpClient.SendAsync"/>,然后等待
    /// 批准者, 并返回服务器应用过多少次请求和批准者 。
    /// </summary>
    public async DurableTask<string> ApplyEffectAsync(string id)
    {
        var applied = await api.SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"/effects/{id}")).WithId("apply");
        var approver = await Approved.WaitAsync();
        return $"{applied.EnsureSuccessStatusCode().ReadAsString()} {approver}";
    }

    private DurableTask<DurableHttpOperation> StartApprovalAsync(string orderId) =>
        api.StartOperationAsync(() => new HttpRequestMessage(HttpMethod.Post, $"/orders/{orderId}/approval")
        {
            Content = JsonContent.Create(new ApprovalRequest("widget")),
        });
    /// <summary>
    /// 发送 <c>POST</c> 和 <c>GET</c> 请求,并返回该步骤的标识符即“应急”键
    /// 服务器接收到的,以及阶梯的化身。
    /// </summary>
    public async DurableTask<string[]> SendIdempotencyKeyAsync() =>
        await DurableTask.Run(async cancellationToken =>
        {
            var client = httpClients.CreateClient(DurableHttpFixture.RawClientName);
            var taskId = DurableTask.Current.Id.ToString();
            var incarnation = DurableTask.Current.Incarnation.ToString("N");
            using var post = await client.PostAsync("/echo-idempotency-key", content: null, cancellationToken);
            var postKey = await post.Content.ReadAsStringAsync(cancellationToken);
            var getKey = await client.GetStringAsync("/echo-idempotency-key", cancellationToken);
            return new[] { taskId, postKey, getKey, incarnation };
        }).WithId("send");
}

public sealed record EchoRequest(string Value);

public sealed record ApprovalRequest(string Item);

public sealed record NexusApprovalInput(string OrderId, string Item);
