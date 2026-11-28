using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Durable.Tasks;
using Orleans.Hosting;
using Orleans.Durable.Journaling;
using Orleans.Runtime;

namespace Documentation.Grains.DurableTasks;

internal static class DurableTaskConfiguration
{
    internal static void ConfigureSilo(ISiloBuilder siloBuilder)
    {
        // <durable_tasks_registration>
        siloBuilder.UseInMemoryDurableJobs();
        siloBuilder.AddVolatileJournalStorage();
        siloBuilder.AddDurableTasks(options => options.ResultRetentionPeriod = TimeSpan.FromDays(7));
        // </durable_tasks_registration>
    }

    internal static void ConfigureClient(IClientBuilder clientBuilder)
    {
        // <durable_tasks_client_registration>
        clientBuilder.AddDurableTasks();
        // </durable_tasks_client_registration>
    }
}

// <durable_tasks_contracts>
public enum OrderStatus
{
    None,
    Submitted,
    AwaitingValidation,
    Paid,
    Shipped,
    Cancelled,
}

[GenerateSerializer, Immutable]
public sealed record OrderLine(int ProductId, decimal UnitPrice, int Units);

[GenerateSerializer, Immutable]
public sealed record SubmitOrder(string BuyerId, OrderLine[] Lines, string CardLast4);

[GenerateSerializer, Immutable]
public sealed record PaymentResult(bool Succeeded, string? Reason);

[GenerateSerializer, Immutable]
public sealed record OrderResult(OrderStatus Status, string? Description);

[GenerateSerializer, Immutable]
public sealed record Shipment(string Carrier, string TrackingNumber);

public interface ICatalogItemGrain : IGrainWithIntegerKey
{
    Task RestockAsync(int units);

    DurableTask<bool> ReserveAsync(Guid orderId, int units);

    // 返回订单在本次释放前是否持有预留资源，因此释放操作具有幂等性。
    DurableTask<bool> ReleaseAsync(Guid orderId);
}

public interface IPaymentGrain : IGrainWithGuidKey
{
    DurableTask<PaymentResult> AuthorizeAsync(decimal amount, string cardLast4);
}

public interface IOrderGrain : IGrainWithGuidKey
{
    DurableTask<OrderResult> PlaceOrderAsync(SubmitOrder order);

    Task<bool> CancelAsync();

    Task AbortAsync(string reason);

    Task<bool> ShipAsync(Shipment shipment);
}
// </durable_tasks_contracts>

// <durable_tasks_catalog_grain>
public sealed class CatalogItemGrain : DurableGrain, ICatalogItemGrain
{
    private readonly IDurableValue<int> _available;
    private readonly IDurableDictionary<Guid, int> _reservations;

    public CatalogItemGrain()
    {
        _available = StateManager.GetOrAddValue<int>("available");
        _reservations = StateManager.GetOrAddDictionary<Guid, int>("reservations");
    }

    public async Task RestockAsync(int units)
    {
        _available.Value += units;
        await WriteStateAsync();
    }

    // 状态变更和任务结果在同一次日志写入中提交。
    public async DurableTask<bool> ReserveAsync(Guid orderId, int units)
    {
        if (units <= 0 || _available.Value < units)
        {
            return false;
        }

        _available.Value -= units;
        _reservations[orderId] = units;
        return true;
    }

    public async DurableTask<bool> ReleaseAsync(Guid orderId)
    {
        if (!_reservations.Remove(orderId, out var units))
        {
            return false;
        }

        _available.Value += units;
        return true;
    }
}
// </durable_tasks_catalog_grain>

public sealed class PaymentGrain : DurableGrain, IPaymentGrain
{
    public async DurableTask<PaymentResult> AuthorizeAsync(decimal amount, string cardLast4) =>
        cardLast4 == "0000" ? new PaymentResult(false, "The card was declined.") : new PaymentResult(true, null);
}

// <durable_tasks_order_grain>
public sealed class OrderGrain : DurableGrain, IOrderGrain
{
    private static readonly DurableSignal<Shipment> Shipped = new("shipped");

    private readonly IDurableTaskManager _tasks;
    private readonly IDurableValue<OrderStatus> _status;

    public OrderGrain(IDurableTaskManager tasks)
    {
        _tasks = tasks;
        _status = StateManager.GetOrAddValue<OrderStatus>("status");
    }

    private Guid OrderId => this.GetPrimaryKey();

    public static string PlaceOrderTaskId(Guid orderId) => $"place-order-{orderId:N}";

    public async DurableTask<OrderResult> PlaceOrderAsync(SubmitOrder order)
    {
        DurableTask.RunInline("submit", () => SetStatus(OrderStatus.Submitted));

        // <durable_tasks_fan_out>
        bool[] reserved;
        try
        {
            await DurableTask.Delay(TimeSpan.FromMinutes(1)).WithId("grace-period");
            DurableTask.RunInline("validate", () => SetStatus(OrderStatus.AwaitingValidation));

            // 并发预留每个订单行，子任务分别为 reserve/0、reserve/1 等。
            reserved = await order.Lines.Select(line => Catalog(line.ProductId).ReserveAsync(OrderId, line.Units))
                .WhenAll()
                .WithId("reserve");
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            // WhenAll 已等待每个预留操作，因此没有任何预留操作仍在运行。
            return await CancelOrderAsync(order.Lines, DurableTask.Current.TaskCancellationReason);
        }

        if (Array.IndexOf(reserved, false) >= 0)
        {
            return await CancelOrderAsync(order.Lines, "A line is out of stock.");
        }
        // </durable_tasks_fan_out>

        var payment = await GrainFactory.GetGrain<IPaymentGrain>(OrderId)
            .AuthorizeAsync(order.Lines.Sum(line => line.UnitPrice * line.Units), order.CardLast4)
            .WithId("authorize-payment");
        if (!payment.Succeeded)
        {
            return await CancelOrderAsync(order.Lines, payment.Reason);
        }

        DurableTask.RunInline("pay", () => SetStatus(OrderStatus.Paid));

        // 如果发货信号在等待开始前已到达，则立即返回。
        var shipment = await Shipped.WaitAsync();
        DurableTask.RunInline("ship", () => SetStatus(OrderStatus.Shipped));
        return new OrderResult(OrderStatus.Shipped, shipment.TrackingNumber);
    }

    // <durable_tasks_order_controls>
    public async Task<bool> CancelAsync()
    {
        if (_status.Value is not (OrderStatus.Submitted or OrderStatus.AwaitingValidation))
        {
            return false;
        }

        await _tasks.CancelAsync(PlaceOrderTaskId(OrderId), "Cancelled by the buyer.");
        return true;
    }

    public async Task AbortAsync(string reason) => await _tasks.AbortAsync(PlaceOrderTaskId(OrderId), reason);

    public async Task<bool> ShipAsync(Shipment shipment)
    {
        if (_status.Value is not OrderStatus.Paid)
        {
            return false;
        }

        var result = await _tasks.TrySignalAsync(PlaceOrderTaskId(OrderId), Shipped, shipment);
        return result is DurableSignalResult.Accepted or DurableSignalResult.Duplicate;
    }
    // </durable_tasks_order_controls>

    // <durable_tasks_compensation>
    private async Task<OrderResult> CancelOrderAsync(OrderLine[] lines, string? reason)
    {
        // 释放操作受取消屏蔽保护，因此即使取消已结束工作流，该操作仍运行至完成。
        await lines.Select(line => Catalog(line.ProductId).ReleaseAsync(OrderId))
            .WhenAll()
            .WithId("release")
            .Shielded();
        DurableTask.RunInline("cancel", () => SetStatus(OrderStatus.Cancelled));
        return new OrderResult(OrderStatus.Cancelled, reason);
    }
    // </durable_tasks_compensation>

    // <durable_tasks_run_inline>
    // 工作流将每次状态变更作为已记录的效果运行，例如 DurableTask.RunInline("pay", ...)：状态变更
    // 与记录的结果一同提交，重放直接返回结果，而不再变更状态。
    private OrderStatus SetStatus(OrderStatus status)
    {
        _status.Value = status;
        return status;
    }
    // </durable_tasks_run_inline>

    private ICatalogItemGrain Catalog(int productId) => GrainFactory.GetGrain<ICatalogItemGrain>(productId);
}
// </durable_tasks_order_grain>

// <durable_tasks_schedule_from_grain>
public interface IBasketGrain : IGrainWithStringKey
{
    Task AddAsync(OrderLine line);

    Task<Guid?> CheckoutAsync(Guid orderId, string cardLast4);

    Task<OrderResult?> GetOrderResultAsync(Guid orderId);
}

public sealed class BasketGrain : DurableGrain, IBasketGrain
{
    private readonly IDurableDictionary<int, OrderLine> _lines;

    public BasketGrain() => _lines = StateManager.GetOrAddDictionary<int, OrderLine>("lines");

    public async Task AddAsync(OrderLine line)
    {
        _lines[line.ProductId] = line;
        await WriteStateAsync();
    }

    public async Task<Guid?> CheckoutAsync(Guid orderId, string cardLast4)
    {
        if (_lines.Count == 0)
        {
            return null;
        }

        var order = new SubmitOrder(this.GetPrimaryKeyString(), [.. _lines.Values], cardLast4);

        // <durable_tasks_checkout_post>
        _lines.Clear();

        // 将调用投递到发件箱，日志写入将它与已清空的购物车一同提交。
        _ = GrainFactory.GetGrain<IOrderGrain>(orderId)
            .PlaceOrderAsync(order)
            .WithId(OrderGrain.PlaceOrderTaskId(orderId))
            .Post();
        await WriteStateAsync();
        // </durable_tasks_checkout_post>
        return orderId;
    }

    // 轮询订单 Grain 上的工作流，该 Grain 保留工作流结果。
    public async Task<OrderResult?> GetOrderResultAsync(Guid orderId)
    {
        try
        {
            var response = await GrainFactory.GetGrain<IOrderGrain>(orderId)
                .GetDurableTask<OrderResult>(OrderGrain.PlaceOrderTaskId(orderId))
                .GetResponseAsync(new PollingOptions { PollTimeout = TimeSpan.Zero });
            return response.Status is DurableTaskStatus.CompletedSuccessfully ? response.GetResult<OrderResult>() : null;
        }
        catch (DurableTaskNotFoundException)
        {
            // 可能没有结账操作启动此订单、结账调用尚未到达订单，或者结果的
            // 保留期已结束。
            return null;
        }
    }
}
// </durable_tasks_schedule_from_grain>

internal static class OrderClient
{
    // <durable_tasks_schedule_from_client>
    public static async Task<OrderResult> PlaceOrderAsync(IClusterClient client, Guid orderId, SubmitOrder order)
    {
        // 使用相同根 ID 再次调度同一请求会返回同一任务，因此重试是安全的。
        ScheduledTask<OrderResult> workflow = await client.GetGrain<IOrderGrain>(orderId)
            .PlaceOrderAsync(order)
            .WithId(OrderGrain.PlaceOrderTaskId(orderId))
            .ScheduleAsync();
        return await workflow;
    }

    public static async Task<DurableTaskStatus> GetStatusAsync(IClusterClient client, Guid orderId)
    {
        var workflow = client.GetGrain<IOrderGrain>(orderId)
            .GetDurableTask<OrderResult>(OrderGrain.PlaceOrderTaskId(orderId));
        return await workflow.GetStatusAsync(new PollingOptions { PollTimeout = TimeSpan.Zero });
    }
    // </durable_tasks_schedule_from_client>

    // <durable_tasks_client_controls>
    public static async Task CancelAsync(IClusterClient client, Guid orderId, string reason)
    {
        var workflow = client.GetGrain<IOrderGrain>(orderId)
            .GetDurableTask<OrderResult>(OrderGrain.PlaceOrderTaskId(orderId));
        await workflow.CancelAsync(reason);
    }

    public static async Task AbortAsync(IClusterClient client, Guid orderId, string reason)
    {
        var workflow = client.GetGrain<IOrderGrain>(orderId)
            .GetDurableTask<OrderResult>(OrderGrain.PlaceOrderTaskId(orderId));
        await workflow.AbortAsync(reason);
    }
    // </durable_tasks_client_controls>
}

// <durable_tasks_race>
[GenerateSerializer, Immutable]
public sealed record ShippingQuote(string Carrier, decimal Price);

public interface ICarrierGrain : IGrainWithStringKey
{
    // 承运人不提供该线路的服务时返回 null。
    DurableTask<ShippingQuote?> QuoteAsync(string route);
}

public interface IShippingGrain : IGrainWithGuidKey
{
    DurableTask<ShippingQuote?> QuoteFirstAsync(string route);

    DurableTask<ShippingQuote?> QuoteGroundAsync(string route);

    DurableTask<ShippingQuote[]> CollectQuotesAsync(string route, string[] carriers, int count);
}

public sealed partial class ShippingGrain : DurableGrain, IShippingGrain
{
    // 等另一承运人的请求结束后，返回最先响应的承运人的报价。
    public async DurableTask<ShippingQuote?> QuoteFirstAsync(string route)
    {
        var (_, quote) = await DurableTask.Race([Carrier("ground").QuoteAsync(route), Carrier("air").QuoteAsync(route)])
            .WithId("first-quote");
        return quote;
    }

    private ICarrierGrain Carrier(string name) => GrainFactory.GetGrain<ICarrierGrain>(name);
}
// </durable_tasks_race>

public sealed partial class ShippingGrain
{
    // <durable_tasks_timeout>
    public async DurableTask<ShippingQuote?> QuoteGroundAsync(string route)
    {
        try
        {
            return await Carrier("ground").QuoteAsync(route).WaitAsync(TimeSpan.FromMinutes(2)).WithId("ground-quote");
        }
        catch (TimeoutException)
        {
            // 超时请求取消承运人的请求后，该请求以取消状态结束。
            return null;
        }
    }
    // </durable_tasks_timeout>
}

public sealed partial class ShippingGrain
{
    // <durable_tasks_when_each>
    public async DurableTask<ShippingQuote[]> CollectQuotesAsync(string route, string[] carriers, int count)
    {
        var requests = await carriers.Select(carrier => Carrier(carrier).QuoteAsync(route))
            .ScheduleAll()
            .WithId("quotes");

        var quotes = new List<ShippingQuote>();
        await foreach (var request in ScheduledTask.WhenEach(requests))
        {
            if (request.TryGetResult(out var quote) && quote is not null)
            {
                quotes.Add(quote);
                if (quotes.Count == count)
                {
                    break;
                }
            }
        }

        // 取消已经完成的请求不会产生变化。
        foreach (var request in requests)
        {
            await request.CancelAsync("Enough quotes were collected.");
        }

        return [.. quotes];
    }
    // </durable_tasks_when_each>
}

// <durable_tasks_when_all_completed>
public interface IAllocationGrain : IGrainWithGuidKey
{
    DurableTask<bool> AllocateAsync(OrderLine[] lines);
}

public sealed class AllocationGrain : DurableGrain, IAllocationGrain
{
    private Guid OrderId => this.GetPrimaryKey();

    public async DurableTask<bool> AllocateAsync(OrderLine[] lines)
    {
        var reservations = await lines.Select(line => Catalog(line.ProductId).ReserveAsync(OrderId, line.Units))
            .WhenAllCompleted()
            .WithId("reserve");
        if (reservations.All(reservation => reservation.TryGetResult(out var reserved) && reserved))
        {
            return true;
        }

        // 仅释放预留成功的订单行所对应的资源，即使已经收到取消请求也是如此。
        var held = lines.Where((_, i) => reservations[i].TryGetResult(out var reserved) && reserved);
        await held.Select(line => Catalog(line.ProductId).ReleaseAsync(OrderId))
            .WhenAll()
            .WithId("release")
            .Shielded();

        DurableTask.Current.CancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    private ICatalogItemGrain Catalog(int productId) => GrainFactory.GetGrain<ICatalogItemGrain>(productId);
}
// </durable_tasks_when_all_completed>

// <durable_tasks_schedule_all>
[GenerateSerializer, Immutable]
public sealed record Package(string PackageId, decimal WeightKg);

public interface ILabelGrain : IGrainWithStringKey
{
    DurableTask<string> PrintAsync(Package package);
}

public interface IFulfillmentGrain : IGrainWithGuidKey
{
    DurableTask<string[]> FulfillAsync(Package[] packages);
}

public sealed class FulfillmentGrain : DurableGrain, IFulfillmentGrain
{
    private readonly IDurableValue<bool> _picked;

    public FulfillmentGrain() => _picked = StateManager.GetOrAddValue<bool>("picked");

    public async DurableTask<string[]> FulfillAsync(Package[] packages)
    {
        // 开始打印每个标签，子任务分别为 labels/0、labels/1 等，不等待它们完成。
        var labels = await packages.Select(package => GrainFactory.GetGrain<ILabelGrain>(package.PackageId).PrintAsync(package))
            .ScheduleAll()
            .WithId("labels");

        await MarkPicked().WithId("pick");

        // 按输入顺序抛出第一个失败；若无失败，则抛出第一个取消异常。
        await ScheduledTask.WhenAll(labels);
        return [.. labels.Select(label => label.GetResult())];
    }

    private async DurableTask<bool> MarkPicked() => _picked.Value = true;
}
// </durable_tasks_schedule_all>

// <durable_tasks_signal>
[GenerateSerializer, Immutable]
public sealed record ExpenseClaim(string EmployeeId, decimal Amount);

[GenerateSerializer, Immutable]
public sealed record Decision(string ApproverId, bool Approved);

public enum ExpenseOutcome
{
    Approved,
    Rejected,
    Expired,
}

public interface IExpenseGrain : IGrainWithGuidKey
{
    DurableTask<ExpenseOutcome> SubmitAsync(ExpenseClaim claim);

    Task<DurableSignalResult> DecideAsync(Decision decision);
}

public sealed class ExpenseGrain(IDurableTaskManager tasks) : DurableGrain, IExpenseGrain
{
    public static readonly DurableSignal<Decision> Decided = new("decided");

    public static string SubmitTaskId(Guid expenseId) => $"expense-{expenseId:N}";

    public async DurableTask<ExpenseOutcome> SubmitAsync(ExpenseClaim claim)
    {
        // 逻辑时间，重放观察到的值保持不变。
        var deadline = DurableTask.Current.UtcNow.AddDays(3);
        var (received, decision) = await Decided.TryWaitAsync(deadline).WithId("decision");
        if (!received)
        {
            return ExpenseOutcome.Expired;
        }

        return decision.Approved ? ExpenseOutcome.Approved : ExpenseOutcome.Rejected;
    }

    public async Task<DurableSignalResult> DecideAsync(Decision decision) =>
        await tasks.TrySignalAsync(SubmitTaskId(this.GetPrimaryKey()), Decided, decision);
}
// </durable_tasks_signal>

internal static class ExpenseClient
{
    // <durable_tasks_signal_client>
    public static async Task<DurableSignalResult> DecideAsync(IClusterClient client, Guid expenseId, Decision decision) =>
        await client.GetGrain<IExpenseGrain>(expenseId).TrySignalDurableTaskAsync(
            ExpenseGrain.SubmitTaskId(expenseId),
            ExpenseGrain.Decided,
            decision);
    // </durable_tasks_signal_client>
}

// <durable_tasks_detach>
[GenerateSerializer, Immutable]
public sealed record InvoiceRequest(string CustomerId, decimal Amount);

public interface INotificationGrain : IGrainWithStringKey
{
    // 返回本次调用是否发送了收据，因此重复发送收据为空操作。
    DurableTask<bool> SendReceiptAsync(Guid invoiceId, decimal amount);
}

public interface IInvoiceGrain : IGrainWithGuidKey
{
    DurableTask<decimal> IssueAsync(InvoiceRequest request);
}

public sealed class InvoiceGrain : DurableGrain, IInvoiceGrain
{
    private readonly IDurableValue<decimal> _issued;

    public InvoiceGrain() => _issued = StateManager.GetOrAddValue<decimal>("issued");

    public static string ReceiptTaskId(Guid invoiceId) => $"receipt-{invoiceId:N}";

    public async DurableTask<decimal> IssueAsync(InvoiceRequest request)
    {
        var amount = await Record(request.Amount).WithId("record");

        // 在通知 Grain 上启动根任务 "receipt-{invoiceId}"，并将启动操作与发票任务的结果一同提交。
        _ = GrainFactory.GetGrain<INotificationGrain>(request.CustomerId)
            .SendReceiptAsync(this.GetPrimaryKey(), amount)
            .Detached(ReceiptTaskId(this.GetPrimaryKey()))
            .Post();
        return amount;
    }

    private async DurableTask<decimal> Record(decimal amount) => _issued.Value = amount;
}
// </durable_tasks_detach>

// <durable_tasks_replay_regions>
[GenerateSerializer, Immutable]
public sealed record Ticket(int Number, Guid Reference, DateTimeOffset IssuedAt);

public interface ITicketGrain : IGrainWithStringKey
{
    DurableTask<Ticket> IssueAsync(decimal amount, string cardLast4);
}

public sealed class TicketGrain : DurableGrain, ITicketGrain
{
    private readonly ILogger _logger;
    private readonly IDurableValue<int> _lastNumber;
    private readonly IDurableDictionary<int, bool> _paid;

    public TicketGrain(ILogger<TicketGrain> logger)
    {
        // 在任务重放期间跳过日志事件，因此每个事件只记录一次。
        _logger = logger.ReplaySafe();
        _lastNumber = StateManager.GetOrAddValue<int>("last-number");
        _paid = StateManager.GetOrAddDictionary<int, bool>("paid");
    }

    public async DurableTask<Ticket> IssueAsync(decimal amount, string cardLast4)
    {
        // 重新执行的代码：每次重放都会运行，但通过重放安全的日志记录器只记录一次日志。
        _logger.LogInformation("Issuing a ticket for {Amount}.", amount);

        // 已记录的效果递增计数器、生成引用编号并读取一次时钟。计数器的变更
        // 与记录的凭证一同提交，重放直接返回该凭证，而不会再次运行效果。
        var ticket = DurableTask.RunInline(
            "allocate",
            () => new Ticket(++_lastNumber.Value, Guid.NewGuid(), DateTimeOffset.UtcNow));

        var payment = await GrainFactory.GetGrain<IPaymentGrain>(ticket.Reference)
            .AuthorizeAsync(amount, cardLast4)
            .WithId("authorize");

        // 末尾的同步代码：此变更之后没有持久化操作，因此与任务结果一同提交。
        _paid[ticket.Number] = payment.Succeeded;
        return ticket;
    }
}
// </durable_tasks_replay_regions>

// <durable_tasks_effect_and_step>
public interface IHoldGrain : IGrainWithStringKey
{
    DurableTask<Guid> HoldAsync(int quantity);

    DurableTask<Guid> ConfirmAsync(Guid holdId);
}

public sealed class HoldGrain : DurableGrain, IHoldGrain
{
    private readonly IDurableValue<int> _held;
    private readonly IDurableValue<Guid> _holdId;

    public HoldGrain()
    {
        _held = StateManager.GetOrAddValue<int>("held");
        _holdId = StateManager.GetOrAddValue<Guid>("hold-id");
    }

    public async DurableTask<Guid> HoldAsync(int quantity)
    {
        // 每个效果都在当前工作项返回前运行，并分别记录在 {taskId}/hold-id 和 {taskId}/hold 下。
        // 它们的记录及状态变更在同一次日志写入中提交，期间不执行其他轮次。
        var holdId = DurableTask.RunInline("hold-id", Guid.NewGuid);
        DurableTask.RunInline("hold", () =>
        {
            _held.Value += quantity;
            _holdId.Value = holdId;
        });

        await DurableTask.Delay(TimeSpan.FromMinutes(15)).WithId("expiry");
        return holdId;
    }

    public async DurableTask<Guid> ConfirmAsync(Guid holdId)
    {
        // 在等待步骤时，它作为具有独立上下文的子执行实例运行，并记录在
        // {taskId}/confirmation-id，即 DurableTask.RunInline("confirmation-id", ...) 所使用的标识符。
        return await DurableTask.Run(async () => await RequestConfirmationAsync(holdId)).WithId("confirmation-id");
    }

    private static Task<Guid> RequestConfirmationAsync(Guid holdId) => Task.FromResult(holdId);
}
// </durable_tasks_effect_and_step>

// <durable_tasks_progress>
[GenerateSerializer, Immutable]
public sealed record ExportProgress(int Completed, int Total);

public interface IPartitionGrain : IGrainWithStringKey
{
    DurableTask ExportAsync();
}

public interface IExportGrain : IGrainWithStringKey
{
    DurableTask<int> ExportAsync(string[] partitions);

    IAsyncEnumerable<ExportProgress> WatchAsync(CancellationToken cancellationToken);
}

public sealed class ExportGrain : DurableGrain, IExportGrain
{
    private readonly IDurableLog<ExportProgress> _progress;

    public ExportGrain() => _progress = StateManager.GetOrAddLog<ExportProgress>("progress");

    public async DurableTask<int> ExportAsync(string[] partitions)
    {
        // 每次追加仅运行一次，重放返回记录的序号，因此不会再次发布旧进度。
        // 循环中的追加操作共用一个名称，分别标识为 progress、progress$2 等。
        DurableTask.RunInline(() => _progress.Append(new ExportProgress(0, partitions.Length)));
        for (var i = 0; i < partitions.Length; i++)
        {
            await GrainFactory.GetGrain<IPartitionGrain>(partitions[i]).ExportAsync().WithId("partition");
            DurableTask.RunInline("progress", () => _progress.Append(new ExportProgress(i + 1, partitions.Length)));
        }

        return partitions.Length;
    }

    // 观察者从首条记录开始，按顺序读取已提交条目。
    public async IAsyncEnumerable<ExportProgress> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var change in _progress.ReadAsync(cancellationToken: cancellationToken))
        {
            if (change is { Kind: LogEventKind.Entry, Item: { } progress })
            {
                yield return progress;
                if (progress.Completed == progress.Total)
                {
                    yield break;
                }
            }
        }
    }
}
// </durable_tasks_progress>

// <durable_tasks_stream_draft>
public interface IChatModel
{
    IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken cancellationToken);
}

public interface IConversationGrain : IGrainWithStringKey
{
    DurableTask<long> AskAsync(string prompt);

    IAsyncEnumerable<LogEvent<string>> WatchAsync(long fromSequence, long epoch, CancellationToken cancellationToken);
}

public sealed class ConversationGrain : DurableGrain, IConversationGrain
{
    private readonly IChatModel _model;
    private readonly IDurableLog<string> _transcript;

    public ConversationGrain(IChatModel model)
    {
        _model = model;
        _transcript = StateManager.GetOrAddLog<string>("transcript");
    }

    // 返回已提交响应的序列号。
    public async DurableTask<long> AskAsync(string prompt) => await DurableTask.Run(async cancellationToken =>
    {
        using var draft = _transcript.BeginDraft();
        var response = new StringBuilder();
        await foreach (var chunk in _model.StreamAsync(prompt, cancellationToken))
        {
            // 易失性：观察者在每个数据块到达时接收它，日志写入不会包含它。
            draft.Write(chunk);
            response.Append(chunk);
        }

        // 步骤末尾的同步代码：此条目与步骤结果在同一次日志写入中提交。
        return draft.Commit(response.ToString());
    }).WithId("respond");

    // 观察者实时接收草稿数据块，随后接收替换这些数据块的已提交条目。
    public IAsyncEnumerable<LogEvent<string>> WatchAsync(long fromSequence, long epoch, CancellationToken cancellationToken) =>
        _transcript.ReadAsync(fromSequence, epoch, includeDrafts: true, cancellationToken: cancellationToken);
}
// </durable_tasks_stream_draft>

internal static class InvoiceClient
{
    // <durable_tasks_detach_observe>
    public static async Task<DurableTaskStatus> GetReceiptStatusAsync(IClusterClient client, Guid invoiceId, string customerId)
    {
        var receipt = client.GetGrain<INotificationGrain>(customerId).GetDurableTask<bool>(InvoiceGrain.ReceiptTaskId(invoiceId));
        return await receipt.GetStatusAsync(new PollingOptions { PollTimeout = TimeSpan.Zero });
    }
    // </durable_tasks_detach_observe>
}

internal static class DurableTaskDiagnostics
{
    // <durable_tasks_diagnostics>
    public static async Task PrintOrderTaskAsync(IGrainFactory grains, Guid orderId)
    {
        var workflow = grains.GetGrain<IOrderGrain>(orderId).GetDurableTask<OrderResult>(OrderGrain.PlaceOrderTaskId(orderId));
        try
        {
            var response = await workflow.GetResponseAsync(new PollingOptions { PollTimeout = TimeSpan.Zero });
            Console.WriteLine($"{workflow.Address}: {response.Status} {response.Exception?.Message}");
        }
        catch (DurableTaskNotFoundException)
        {
            Console.WriteLine($"{workflow.Address}: not retained");
        }
    }
    // </durable_tasks_diagnostics>
}

// <durable_tasks_reference_relay>
[GenerateSerializer, Immutable]
public sealed record OperationResult(string Summary);

public interface IOperationGrain : IGrainWithGuidKey
{
    DurableTask<OperationResult> RunAsync(string input);
}

public interface IWebhookRelayGrain : IGrainWithGuidKey
{
    DurableTask DeliverWebhookAsync(Guid operationKey, string operationId, Uri callback);
}

public sealed class WebhookRelayGrain(HttpClient http) : DurableGrain, IWebhookRelayGrain
{
    public async DurableTask DeliverWebhookAsync(Guid operationKey, string operationId, Uri callback)
    {
        var operation = GrainFactory.GetGrain<IOperationGrain>(operationKey);
        try
        {
            // 订阅操作结果；订阅及其截止时间均被记录，以供重放使用。
            var result = await operation.GetDurableTask<OperationResult>(operationId).WaitAsync(TimeSpan.FromDays(1));
            await DurableTask.Run(async ct =>
            {
                using var response = await http.PostAsJsonAsync(callback, result, ct);
                response.EnsureSuccessStatusCode();
            }).WithId("deliver");
        }
        catch (TimeoutException)
        {
            // 一天后操作仍在运行；即使没有当前中继，它也会继续运行。
        }
    }
}
// </durable_tasks_reference_relay>
