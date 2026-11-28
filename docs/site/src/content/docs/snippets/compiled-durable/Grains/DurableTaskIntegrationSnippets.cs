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

// 模拟支付提供程序客户端（例如 Stripe 的客户端），该客户端根据幂等键对请求去重。
public interface IPaymentProvider
{
    Task<ChargeResult> ChargeAsync(string customerId, decimal amount, string idempotencyKey, CancellationToken cancellationToken);
}

// 提供程序拒绝扣款，例如银行卡被拒付。重试返回相同的拒绝结果。
public sealed class PaymentDeclinedException(string reason) : Exception(reason);

// 提供程序当前无法处理请求，例如由于速率限制或服务中断。
public sealed class PaymentUnavailableException(TimeSpan? retryAfter) : Exception("The payment provider is unavailable.")
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
// </durable_tasks_payment_provider>

// <durable_tasks_charge_outcome>
// 步骤以值的形式返回工作流需要处理的结果，因此重放观察到相同结果。
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
    // Grain、任务 ID 及运行世代标识在每次重放和重试中唯一标识本次运行。操作的
    // 名称用于区分本次运行的不同外部请求。
    private string IdempotencyKey(string operation) =>
        $"{this.GetGrainId()}/{DurableTask.Current.Id}/{DurableTask.Current.Incarnation:N}/{operation}";
    // </durable_tasks_idempotency_key>

    // <durable_tasks_charge_in_step>
    public async DurableTask<ChargeResult?> ChargeAsync(string customerId, decimal amount)
    {
        var key = IdempotencyKey("charge");
        var outcome = await DurableTask.Run(async cancellationToken =>
        {
            // 在步骤内重试短暂的瞬时故障。每次尝试发送相同的键，因此提供程序
            // 只扣款一次，无论多少次尝试到达它，包括步骤重放时重复的尝试。
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
            // 每次尝试都是一个步骤，分别为 "charge"、"charge$2" 等，均发送相同的键。
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
                // 扣款成功或被拒绝：被拒付的银行卡仍为拒付状态，因此工作流不会重试。
                return outcome.Charge;
            }

            if (DurableTask.Current.UtcNow + retryAfter > deadline)
            {
                throw new TimeoutException("The payment provider stayed unavailable for six hours.");
            }

            // 持久化延迟：等待期间 Grain 可以停用，并在延迟到期时恢复工作流。
            await DurableTask.Delay(retryAfter).WithId("charge-backoff");
        }
    }
    // </durable_tasks_charge_with_backoff>
}

// <durable_tasks_database>
[GenerateSerializer, Immutable]
public sealed record ShipmentRecord(Guid OrderId, string Carrier, string TrackingNumber);

// 模拟关系数据库之上的数据访问层。
public interface IShipmentStore
{
    // INSERT ... ON CONFLICT (operation_id) DO NOTHING：使用相同操作 ID 重复写入不会造成任何变化。
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

        // 在步骤中读取：工作流根据已记录的行作出决定，因此重放采用相同路径。
        var existing = await DurableTask.Run(ct => store.FindAsync(orderId, ct)).WithId("find");
        if (existing is not null)
        {
            return existing;
        }

        // 使用任务派生的 ID 作为写入键，因此重放重复执行的写入不会产生变化。
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

// 应用程序在提交自身变更的同一个事务中，将此行提交到数据库发件箱表。
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
            // 该行的 ID 用作任务名称，因此在 MarkSentAsync 之前崩溃并再次发送此行的中继
            // 会到达同一个任务，该任务只运行一次。
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
        // 一个步骤：其请求携带由此步骤标识派生的 Idempotency-Key，因此步骤的重试或重放
        // 都会到达首次尝试启动的作业。
        var job = await labels.StartOperationAsync(() => new HttpRequestMessage(HttpMethod.Post, "labels")
        {
            Content = JsonContent.Create(new { address }),
        });

        try
        {
            // 在步骤中读取作业的状态事件，并由步骤记录结果；经过一天的
            // 逻辑时间后放弃等待。
            var label = await labels.WaitForOperationAsync<LabelResult>(job, new() { Mode = DurableHttpWaitMode.Events })
                .WaitAsync(TimeSpan.FromDays(1));
            return label.Url;
        }
        catch (Exception exception) when (exception is TimeoutException || DurableTask.Current.IsTaskCancellationRequested && exception is OperationCanceledException)
        {
            // 取消远程作业，随后让当前任务以超时或取消状态结束。
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
        // 构建器添加的处理程序（例如弹性处理程序）位于幂等键处理程序之后，因此这些处理程序的重试
        // 会携带该键。
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

        // 每个 POST 都将 Grain 调用作为一个操作启动，该操作由请求的 Idempotency-Key 在调用方
        // 作用域内命名；路由组在以下路径下提供其状态监视、事件、取消及信号：
        // /returns/{returnId}/operations/{id}。作用域 "returns" 为操作命名，因此重命名路由不影响这些操作。
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
        // 提前到达的 Webhook 会被记录，之后开始等待时立即返回该 Webhook。
        var (received, confirmation) = await Confirmed.TryWaitAsync(DurableTask.Current.UtcNow.AddDays(1)).WithId("confirmation");
        return received && confirmation.Succeeded;
    }

    // 由已验证 Webhook 签名的 Web 端点调用。提供程序再次投递同一事件时
    // 会收到 Duplicate，端点按 Accepted 的方式对此予以确认。
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
            // Messaging 会重试投递启动请求，直到目标接受，因此工作流不需要重试。
            var survey = await GrainFactory.GetGrain<IExpenseGrain>(orderId)
                .SubmitAsync(new ExpenseClaim("survey", 0))
                .Detached($"survey-{orderId:N}")
                .ScheduleAsync();
            return survey.TargetTaskId.ToString();
        }
        catch (DurableTaskConflictException)
        {
            // 此 ID 已为另一请求命名，每次重试都会遇到相同的冲突。
            return "conflict";
        }
        catch (DurableTaskDeliveryException)
        {
            // 启动请求未在 Messaging 的 OutboxRetryTimeout 内被接受。
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

    // 普通方法，可由持久化任务以外的调用方重试：操作 ID 与入账操作一同提交，
    // 因此重试会找到该操作，并返回它产生的余额。
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
