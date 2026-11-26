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

    // 返回命令是否持有保留,因此释放是无效的。
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

    // 国家变化和任务结果 在一个日记中承诺。
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

            // 保留每一行,作为儿童储备/0、储备/1等。
            reserved = await order.Lines.Select(line => Catalog(line.ProductId).ReserveAsync(OrderId, line.Units))
                .WhenAll()
                .WithId("reserve");
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            // WhenAll 每一个保留都等待着,所以没有哪个保留在运行。
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

        // 当货物在等待前到达时立即返回。
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
        // 发布内容被屏蔽, 因此它会持续到结束工作流程的取消后完成。
        await lines.Select(line => Catalog(line.ProductId).ReleaseAsync(OrderId))
            .WhenAll()
            .WithId("release")
            .Shielded();
        DurableTask.RunInline("cancel", () => SetStatus(OrderStatus.Cancelled));
        return new OrderResult(OrderStatus.Cancelled, reason);
    }
    // </durable_tasks_compensation>

    // <durable_tasks_run_inline>
    // 工作流程将每个状态变化作为记录效果运行,例如 DurableTask.RunInline("工资"), : 国家变化
    // 并重播结果返回结果,而不改变国家。
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

        // 张贴引用发信箱的招贴,
        _ = GrainFactory.GetGrain<IOrderGrain>(orderId)
            .PlaceOrderAsync(order)
            .WithId(OrderGrain.PlaceOrderTaskId(orderId))
            .Post();
        await WriteStateAsync();
        // </durable_tasks_checkout_post>
        return orderId;
    }

    // 按顺序 grain抽出工作流程,保留结果。
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
            // 没有出局启动此命令, 出局的报价还没有达到订单, 或者结果还没到
            // 保留期已终止。
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
        // 将相同的请求排入同一根代号下再次返回相同的任务, 这样重试是安全的 。
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
    // 当承运人不为路线服务时返回空 。
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
    // 一旦另一承运人的要求解决,返回第一承运人的报价。
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
            // 在暂停要求取消之后,承运人的要求被终止。
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

        // 取消已经完成更改的请求。
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

        // 确切地释放出保留成功行的界线,包括在取消请求之后。
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
        // 开始打印每个标签, 儿童标签/0, 标签/1,等等, 不等待他们。
        var labels = await packages.Select(package => GrainFactory.GetGrain<ILabelGrain>(package.PackageId).PrintAsync(package))
            .ScheduleAll()
            .WithId("labels");

        await MarkPicked().WithId("pick");

        // 将第一个失败丢弃在输入顺序中, 否则是第一个取消 。
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
        // 逻辑时间,重播时不改变。
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
    // 返回此呼叫是否发送了收据, 所以重复收到是无操作的 。
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

        // 启动根任务“收到 - {”invoiceId」在通知上 grain,其中含有发票结果。
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
        // 任务重放时跳过事件, 所以每个事件都记录一次 。
        _logger = logger.ReplaySafe();
        _lastNumber = StateManager.GetOrAddValue<int>("last-number");
        _paid = StateManager.GetOrAddDictionary<int, bool>("paid");
    }

    public async DurableTask<Ticket> IssueAsync(decimal amount, string cardLast4)
    {
        // 重新执行代码: 它在每个重放中运行, 记录一次通过重播安全记录器。
        _logger.LogInformation("Issuing a ticket for {Amount}.", amount);

        // 记录效果将计数器提前、引用并读过时钟一次。
        // 并重播回票,
        var ticket = DurableTask.RunInline(
            "allocate",
            () => new Ticket(++_lastNumber.Value, Guid.NewGuid(), DateTimeOffset.UtcNow));

        var payment = await GrainFactory.GetGrain<IPaymentGrain>(ticket.Reference)
            .AuthorizeAsync(amount, cardLast4)
            .WithId("authorize");

        // 同步尾巴:没有持久的操作 跟随这一变化, 所以它承诺任务的结果。
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
        // 每个效果在返回之前运行于此工作项中,记录在 {taskId}/ holds-id和{taskId}/holds下。
        // 记录和状态变化在同一个日记中承诺, 彼此之间没有其他转弯。
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
        // 一步在等待时,作为带有自身背景和记录的儿童处决,该步骤在等待阶段运行
        // {taskId}/确认-id, DurableTask.RunInline(“确认-id”,......)将使用的识别符号。
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
        // 每个附件运行一次, 重播返回其记录序列, 这样它就不会发布旧的进度 。
        // 循环的附加附件有一个共同的名字, 它识别它们为进步, 进步2,等等。
        DurableTask.RunInline(() => _progress.Append(new ExportProgress(0, partitions.Length)));
        for (var i = 0; i < partitions.Length; i++)
        {
            await GrainFactory.GetGrain<IPartitionGrain>(partitions[i]).ExportAsync().WithId("partition");
            DurableTask.RunInline("progress", () => _progress.Append(new ExportProgress(i + 1, partitions.Length)));
        }

        return partitions.Length;
    }

    // 观察员们从头一个字中按顺序阅读承诺的条目。
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

    // 返回承诺响应的序列编号。
    public async DurableTask<long> AskAsync(string prompt) => await DurableTask.Run(async cancellationToken =>
    {
        using var draft = _transcript.BeginDraft();
        var response = new StringBuilder();
        await foreach (var chunk in _model.StreamAsync(prompt, cancellationToken))
        {
            // 观察者在到达时接收每个块,
            draft.Write(chunk);
            response.Append(chunk);
        }

        // 步骤的最后同步代码: 条目在同一个日记中承诺以该步骤的结果写入此步骤。
        return draft.Commit(response.ToString());
    }).WithId("respond");

    // 观察者们接收草稿的碎片现场直播 然后是承诺的条目 取代它们
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
            // 订阅行动的结果;订阅和截止日期记录重播。
            var result = await operation.GetDurableTask<OperationResult>(operationId).WaitAsync(TimeSpan.FromDays(1));
            await DurableTask.Run(async ct =>
            {
                using var response = await http.PostAsJsonAsync(callback, result, ct);
                response.EnsureSuccessStatusCode();
            }).WithId("deliver");
        }
        catch (TimeoutException)
        {
            // 操作一天后仍在运行;没有此中继继续。
        }
    }
}
// </durable_tasks_reference_relay>
