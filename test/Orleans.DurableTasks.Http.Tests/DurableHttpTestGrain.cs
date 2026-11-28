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

/// <summary>运行被测 HTTP 端点所启动的持久化任务。</summary>
[GrainType("durable-http-test")]
public sealed class DurableHttpTestGrain(
    IHttpClientFactory httpClients,
    [FromKeyedServices(DurableHttpFixture.ApiClientName)] IDurableHttpClient api) : DurableGrain, IDurableHttpTestGrain
{
    public static readonly DurableSignal<string> Approved = new("approved");
    public static readonly DurableSignal<string> Shipped = new("shipped");
    public static readonly DurableSignal<bool> Confirmed = new("confirmed");

    /// <summary>按标识获取当前 Grain 的激活实例，测试在这些实例运行步骤期间将其停用。</summary>
    /// <remarks>
    /// 步骤作为不可交错的请求运行，因此 Grain 调用请求停用时必须等待该步骤。直接停用
    /// 激活实例（例如 Silo 关闭时）则会中止该步骤。
    /// </remarks>
    public static System.Collections.Concurrent.ConcurrentDictionary<GrainId, IGrainContext> Activations { get; } = new();

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Activations[GrainContext.GrainId] = GrainContext;
        return base.OnActivateAsync(cancellationToken);
    }

    public DurableTask<string> EchoAsync(string value) => DurableTask.FromResult(value);

    /// <summary>等待审批人；审批人为 <c>reject</c> 时失败。</summary>
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

    /// <summary>等待审批人，然后等待承运人。</summary>
    public async DurableTask<string> WaitForShipmentAsync(string item)
    {
        var approver = await Approved.WaitAsync();
        var carrier = await Shipped.WaitAsync();
        return $"{item} approved by {approver}, shipped by {carrier}";
    }

    /// <summary>等待确认，确认值的类型为 <see cref="bool"/>。</summary>
    public async DurableTask<string> WaitForConfirmationAsync(string item) => $"{item} confirmed: {await Confirmed.WaitAsync()}";

    /// <summary>在持久化延迟后返回 <paramref name="value"/>，使 HTTP 客户端可以观察到正在运行的操作。</summary>
    public async DurableTask<string> EchoLaterAsync(string value)
    {
        await DurableTask.Delay(TimeSpan.FromMilliseconds(100));
        return value;
    }

    /// <summary>在持久化延迟后完成，不返回结果。</summary>
    public async DurableTask CompleteLaterAsync(string value) => await DurableTask.Delay(TimeSpan.FromMilliseconds(100));

    /// <summary>在持久化延迟后失败。</summary>
    public async DurableTask<string> FailLaterAsync(string value)
    {
        await DurableTask.Delay(TimeSpan.FromMilliseconds(100));
        throw new InvalidOperationException($"{value} failed.");
    }

    /// <summary>启动位于 <paramref name="path"/> 的操作，并持久化地等待其结果。</summary>
    public DurableTask<string> RunRemoteOperationAsync(string path, string value, DurableHttpWaitMode mode) =>
        api.RunOperationAsync<string>(
            () => new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new EchoRequest(value)) },
            new DurableHttpOperationOptions { Mode = mode, PollInterval = TimeSpan.FromMilliseconds(100) });

    /// <summary>
    /// 启动订单审批，读取其运行中状态，使用 <paramref name="approver"/> 批准审批，
    /// 并返回审批结果。
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
    /// 启动订单审批，使用 <paramref name="reason"/> 取消审批，并返回其
    /// 等待操作失败时的错误。
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
    /// 发送 <c>POST</c> 和 <c>GET</c> 请求，使用 <see cref="IDurableHttpClient.SendAsync"/>，并返回
    /// 服务器收到的幂等键。
    /// </summary>
    public async DurableTask<string[]> SendRemoteRequestsAsync()
    {
        var post = await api.SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "/echo-idempotency-key")).WithId("post");
        var get = await api.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "/echo-idempotency-key")).WithId("get");
        return [post.EnsureSuccessStatusCode().ReadAsString(), get.EnsureSuccessStatusCode().ReadAsString()];
    }

    /// <summary>
    /// 在服务器上应用副作用 <paramref name="id"/>，使用 <see cref="IDurableHttpClient.SendAsync"/>，随后等待
    /// 审批人，并返回服务器已应用的请求数量及审批人。
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
    /// 从步骤发送 <c>POST</c> 和 <c>GET</c> 请求，并返回步骤标识符、服务器收到的幂等键
    /// 以及步骤的运行世代标识。
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
