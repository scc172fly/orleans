using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Durable.Tasks;
using Orleans.Durable.Http;
using Orleans.Hosting;
using Orleans.Durable.Journaling;
using Orleans.Runtime;

namespace Documentation.Grains.DurableTasks;

// <durable_tasks_payment_provider>
[GenerateSerializer, Immutable]
public sealed record ChargeResult(string ChargeId, decimal Amount);

// 代理支付提供者的客户,如Strake的客户,该客户用一通权钥匙重复请求。
public interface IPaymentProvider
{
    Task<ChargeResult> ChargeAsync(string customerId, decimal amount, string idempotencyKey, CancellationToken cancellationToken);
}

// 服务提供人拒绝了这项指控,例如因为卡被拒。重新尝试返回相同的拒绝。
public sealed class PaymentDeclinedException(string reason) : Exception(reason);

// 供应商现在无法处理请求,例如因为利率限制或停电。
public sealed class PaymentUnavailableException(TimeSpan? retryAfter) : Exception("The payment provider is unavailable.")
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
// </durable_tasks_payment_provider>

// <durable_tasks_charge_outcome>
// 一步返回工作流程处理的结果作为值,重放时会看到相同的结果。
[GenerateSerializer, Immutable]
public sealed record ChargeOutcome(ChargeResult? Charge, string? DeclineReason, TimeSpan? RetryAfter)
{
    public static ChargeOutcome Charged(ChargeResult charge) => new(charge, null, null);

    public static ChargeOutcome Declined(string reason) => new(null, reason, null);

    public static ChargeOutcome Unavailable(TimeSpan? retryAfter) => new(null, null, retryAfter ?? TimeSpan.FromMinutes(1));
}
// </durable_tasks_charge_outcome>

public interface ICheckoutGrain : IGrainWithGuidKey
{
    DurableTask<ChargeResult?> ChargeAsync(string customerId, decimal amount);

    DurableTask<ChargeResult?> ChargeWithBackoffAsync(string customerId, decimal amount);
}

public sealed class CheckoutGrain(IPaymentProvider payments) : DurableGrain, ICheckoutGrain
{
    // <durable_tasks_idempotency_key>
    // grain、 任务编号及其化身在每次重放和每次重试中都识别了此运行。
    // 名称区分了运行的外部请求。
    private string IdempotencyKey(string operation) =>
        $"{this.GetGrainId()}/{DurableTask.Current.Id}/{DurableTask.Current.Incarnation:N}/{operation}";
    // </durable_tasks_idempotency_key>

    // <durable_tasks_charge_in_step>
    public async DurableTask<ChargeResult?> ChargeAsync(string customerId, decimal amount)
    {
        var key = IdempotencyKey("charge");
        var outcome = await DurableTask.Run(async cancellationToken =>
        {
            // 将步内短的、短暂的失败重试。 每次尝试都发送相同的密钥, 所以提供者
            // 包括重复步骤的企图。
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return ChargeOutcome.Charged(await payments.ChargeAsync(customerId, amount, key, cancellationToken));
                }
                catch (PaymentDeclinedException declined)
                {
                    return ChargeOutcome.Declined(declined.Message);
                }
                catch (PaymentUnavailableException unavailable) when (attempt < 3)
                {
                    await Task.Delay(unavailable.RetryAfter ?? TimeSpan.FromSeconds(attempt), cancellationToken);
                }
                catch (PaymentUnavailableException unavailable)
                {
                    return ChargeOutcome.Unavailable(unavailable.RetryAfter);
                }
            }
        }).WithId("charge");

        return outcome.Charge;
    }
    // </durable_tasks_charge_in_step>

    // <durable_tasks_charge_with_backoff>
    public async DurableTask<ChargeResult?> ChargeWithBackoffAsync(string customerId, decimal amount)
    {
        var key = IdempotencyKey("charge");
        var deadline = DurableTask.Current.UtcNow.AddHours(6);
        while (true)
        {
            // 每一次尝试都是一步,“充电”、“充电2美元”等等, 每一个尝试都发出同样的钥匙。
            var outcome = await DurableTask.Run(async cancellationToken =>
            {
                try
                {
                    return ChargeOutcome.Charged(await payments.ChargeAsync(customerId, amount, key, cancellationToken));
                }
                catch (PaymentDeclinedException declined)
                {
                    return ChargeOutcome.Declined(declined.Message);
                }
                catch (PaymentUnavailableException unavailable)
                {
                    return ChargeOutcome.Unavailable(unavailable.RetryAfter);
                }
            }).WithId("charge");

            if (outcome.RetryAfter is not { } retryAfter)
            {
                // 被充斥或拒绝:卡片停留下降,所以工作流程不会重试。
                return outcome.Charge;
            }

            if (DurableTask.Current.UtcNow + retryAfter > deadline)
            {
                throw new TimeoutException("The payment provider stayed unavailable for six hours.");
            }

            // 长期延误: grain 可在等待期间停止使用,工作流程在延迟到期时恢复。
            await DurableTask.Delay(retryAfter).WithId("charge-backoff");
        }
    }
    // </durable_tasks_charge_with_backoff>
}

// <durable_tasks_database>
[GenerateSerializer, Immutable]
public sealed record ShipmentRecord(Guid OrderId, string Carrier, string TrackingNumber);

// 用于连接数据库的数据存取层。
public interface IShipmentStore
{
    // 冲突(行动)上没有做任何:重复写作,使用同样的操作ID,不会改变任何东西。
    Task UpsertAsync(string operationId, ShipmentRecord shipment, CancellationToken cancellationToken);

    Task<ShipmentRecord?> FindAsync(Guid orderId, CancellationToken cancellationToken);
}

public interface IShipmentGrain : IGrainWithGuidKey
{
    DurableTask<ShipmentRecord> RecordShipmentAsync(string carrier, string trackingNumber);
}

public sealed class ShipmentGrain(IShipmentStore store) : DurableGrain, IShipmentGrain
{
    public async DurableTask<ShipmentRecord> RecordShipmentAsync(string carrier, string trackingNumber)
    {
        var orderId = this.GetPrimaryKey();

        // 一步一步读取:工作流程从记录行决定,因此重播要遵循相同的路径。
        var existing = await DurableTask.Run(ct => store.FindAsync(orderId, ct)).WithId("find");
        if (existing is not null)
        {
            return existing;
        }

        // 以任务产生的 ID 键入的写作,因此重放重复的写作不会改变任何东西。
        var shipment = new ShipmentRecord(orderId, carrier, trackingNumber);
        var operationId = $"{this.GetGrainId()}/{DurableTask.Current.Id}/{DurableTask.Current.Incarnation:N}/insert";
        await DurableTask.Run(ct => store.UpsertAsync(operationId, shipment, ct)).WithId("insert");
        return shipment;
    }
}
// </durable_tasks_database>

// <durable_tasks_outbox_relay>
[GenerateSerializer, Immutable]
public sealed record ShipmentRequested(Guid OrderId, string Carrier);

public interface IShipmentRequestGrain : IGrainWithGuidKey
{
    DurableTask<bool> HandleAsync(ShipmentRequested request);
}

// 应用程序在数据库发箱表格中承诺与自更改相同交易的行。
public sealed record OutboxRow(long Id, ShipmentRequested Request);

public interface IOutboxTable
{
    Task<IReadOnlyList<OutboxRow>> ReadPendingAsync(int count, CancellationToken cancellationToken);

    Task MarkSentAsync(long id, CancellationToken cancellationToken);
}

public sealed class OutboxRelay(IClusterClient client, IOutboxTable outbox)
{
    public async Task RelayAsync(CancellationToken cancellationToken)
    {
        foreach (var row in await outbox.ReadPendingAsync(100, cancellationToken))
        {
            // 列的 ID 标识任务, 即一个中继器, 其在 MarkSentAsync 前崩溃并再次发送行
            // 完成相同的任务, 任务只运行一次 。
            await client.GetGrain<IShipmentRequestGrain>(row.Request.OrderId)
                .HandleAsync(row.Request)
                .WithId($"outbox-{row.Id}")
                .ScheduleAsync(cancellationToken);
            await outbox.MarkSentAsync(row.Id, cancellationToken);
        }
    }
}
// </durable_tasks_outbox_relay>

// <durable_tasks_http>
[GenerateSerializer, Immutable]
public sealed record LabelResult(string Url);

public interface ILabelPrinterGrain : IGrainWithGuidKey
{
    DurableTask<string> PrintLabelAsync(string address);
}

public sealed class LabelPrinterGrain([FromKeyedServices("labels")] IDurableHttpClient labels) : DurableGrain, ILabelPrinterGrain
{
    public async DurableTask<string> PrintLabelAsync(string address)
    {
        // 步骤:其请求载有根据该步骤身份而得出的一个“相关性钥匙”文件,因此,它的请求含有重写或重录
        // 第一步就完成了第一次尝试开始的工作。
        var job = await labels.StartOperationAsync(() => new HttpRequestMessage(HttpMethod.Post, "labels")
        {
            Content = JsonContent.Create(new { address }),
        });

        try
        {
            // 工作状况事件在记录结果的一步内阅读,并在一天后放弃。
            // 逻辑时间。
            var label = await labels.WaitForOperationAsync<LabelResult>(job, new() { Mode = DurableHttpWaitMode.Events })
                .WaitAsync(TimeSpan.FromDays(1));
            return label.Url;
        }
        catch (Exception exception) when (exception is TimeoutException || DurableTask.Current.IsTaskCancellationRequested && exception is OperationCanceledException)
        {
            // 取消远程任务, 然后让此任务结束超时或取消 。
            if (job.Location is { } location)
            {
                await labels.CancelOperationAsync(location, DurableTask.Current.TaskCancellationReason ?? "The label wasn't printed within a day.").Shielded();
            }

            throw;
        }
    }
}

internal static class LabelPrinterConfiguration
{
    internal static void ConfigureSilo(ISiloBuilder siloBuilder, Uri labelService)
    {
        // 建设者添加的处理器,例如抗御能力处理器,跟随键处理器,进行重试
        // 携带钥匙。
        siloBuilder.Services.AddDurableHttpClient("labels")
            .ConfigureHttpClient(client => client.BaseAddress = labelService);
    }
}
// </durable_tasks_http>

// <durable_tasks_http_operations>
public interface IReturnGrain : IGrainWithStringKey
{
    DurableTask<string> ProcessReturnAsync(string reason);
}

internal static class ReturnEndpoints
{
    public static readonly DurableSignal<bool> Inspected = new("inspected");

    internal static void Map(WebApplicationBuilder builder)
    {
        builder.Services.AddDurableHttp();
        var app = builder.Build();

        // 每一个POST都开始 grain 调用,作为请求方在调用者姓名内所拨的主管官员-钥匙名称的一种行动。
        // 其状况监测、事件、取消和信号
        // /返回/{returnId}/行动/{id}。“返回”范围指作业,因此重新命名路线保留了它们。
        var returns = app.MapGroup("/returns/{returnId}")
            .WithDurableOperations("returns")
            .RequireAuthorization("returns");

        returns.MapPost("/process", (string returnId, string reason, IGrainFactory grains) =>
            grains.GetGrain<IReturnGrain>(returnId).ProcessReturnAsync(reason));
        returns.MapDurableSignal(Inspected);
    }
}
// </durable_tasks_http_operations>

// <durable_tasks_webhook>
[GenerateSerializer, Immutable]
public sealed record PaymentConfirmation(string EventId, string ChargeId, bool Succeeded);

public interface IPaymentWorkflowGrain : IGrainWithGuidKey
{
    DurableTask<bool> AwaitPaymentAsync(string chargeId);

    Task<DurableSignalResult> OnPaymentWebhookAsync(PaymentConfirmation confirmation);
}

public sealed class PaymentWorkflowGrain(IDurableTaskManager tasks) : DurableGrain, IPaymentWorkflowGrain
{
    private static readonly DurableSignal<PaymentConfirmation> Confirmed = new("payment-confirmed");

    public static string WorkflowId(Guid paymentId) => $"payment-{paymentId:N}";

    public async DurableTask<bool> AwaitPaymentAsync(string chargeId)
    {
        // 等待记录前的网络捕捉器, 等待立即返回。
        var (received, confirmation) = await Confirmed.TryWaitAsync(DurableTask.Current.UtcNow.AddDays(1)).WithId("confirmation");
        return received && confirmation.Succeeded;
    }

    // 由网络终端点调用, 以校验 Webhook 的签名。 一个提供同样事件的提供者再次发送
    // 接受复制,终点确认其为被接受。
    public async Task<DurableSignalResult> OnPaymentWebhookAsync(PaymentConfirmation confirmation) =>
        await tasks.TrySignalAsync(WorkflowId(this.GetPrimaryKey()), Confirmed, confirmation);
}
// </durable_tasks_webhook>

// <durable_tasks_rejections>
public interface IFollowUpGrain : IGrainWithGuidKey
{
    DurableTask<string> ScheduleFollowUpAsync(Guid orderId);
}

public sealed class FollowUpGrain : DurableGrain, IFollowUpGrain
{
    public async DurableTask<string> ScheduleFollowUpAsync(Guid orderId)
    {
        try
        {
            // 传送信号到目标接受它之前, 工作流量不会重试它 。
            var survey = await GrainFactory.GetGrain<IExpenseGrain>(orderId)
                .SubmitAsync(new ExpenseClaim("survey", 0))
                .Detached($"survey-{orderId:N}")
                .ScheduleAsync();
            return survey.TargetTaskId.ToString();
        }
        catch (DurableTaskConflictException)
        {
            // 身份识别文件已经列出另一个请求,每次重试都达到相同的冲突。
            return "conflict";
        }
        catch (DurableTaskDeliveryException)
        {
            // 开始在Message的 OutboxRetryTimeout中未被接受 。
            return "undelivered";
        }
    }
}
// </durable_tasks_rejections>

// <durable_tasks_deduplicating_receiver>
public interface ILedgerGrain : IGrainWithStringKey
{
    Task<decimal> CreditAsync(string operationId, decimal amount);
}

public sealed class LedgerGrain : DurableGrain, ILedgerGrain
{
    private readonly IDurableValue<decimal> _balance;
    private readonly IDurableDictionary<string, decimal> _applied;

    public LedgerGrain()
    {
        _balance = StateManager.GetOrAddValue<decimal>("balance");
        _applied = StateManager.GetOrAddDictionary<string, decimal>("applied");
    }

    // 一种普通的方法,在耐久任务之外打电话的人可以重新尝试:操作编号和信用承诺一起,
    // 重试可以找到操作并返回其产生的平衡。
    public async Task<decimal> CreditAsync(string operationId, decimal amount)
    {
        if (_applied.TryGetValue(operationId, out var balance))
        {
            return balance;
        }

        _balance.Value += amount;
        _applied[operationId] = _balance.Value;
        await WriteStateAsync();
        return _balance.Value;
    }
}
// </durable_tasks_deduplicating_receiver>
