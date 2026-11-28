using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Durable.Jobs;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;

namespace OrderProcessing.Grains;

public interface IOrderGrain : IGrainWithGuidKey
{
    Task<OrderDetails?> GetAsync();

    Task<bool> CancelAsync();

    Task<bool> ShipAsync();
}

// 遵循 eShop 订单流程的订单流程管理器：
// Submitted（已提交）->（宽限期）-> AwaitingValidation（等待验证）-> StockConfirmed（库存已确认）-> Paid（已支付）-> Shipped（已发货），
// 或在库存预留被拒绝、支付被拒绝，或者买家在库存确认之前取消时进入 Cancelled（已取消）状态。
// 每次状态转换都在一次原子日志写入中变更日志化状态并暂存传出消息。
public sealed class OrderGrain : DurableGrain, IOrderGrain
{
    private readonly IOutbox _outbox;
    private readonly OrderingOptions _options;
    private readonly OrderMessages _messages;
    private readonly TimeProvider _timeProvider;
    private readonly IDurableValue<OrderState> _order;
    private readonly IDurableValue<DateTimeOffset?> _submissionDate;
    private readonly IDurableDictionary<int, StockLineStatus> _stock;

    public OrderGrain(
        IInbox inbox,
        IOutbox outbox,
        IOptions<OrderingOptions> options,
        OrderMessages messages,
        [FromKeyedServices(DurableJobTimeProviderNames.DurableJobs)] TimeProvider timeProvider)
    {
        _outbox = outbox;
        _options = options.Value;
        _messages = messages;
        _timeProvider = timeProvider;
        _order = StateManager.GetOrAddValue<OrderState>("order");
        _submissionDate = StateManager.GetOrAddValue<DateTimeOffset?>("submission-date");
        _stock = StateManager.GetOrAddDictionary<int, StockLineStatus>("stock");
        inbox.Register(messages.SubmitOrder, HandleSubmit)
            .Register(messages.GracePeriodElapsed, HandleGracePeriodElapsed)
            .Register(messages.StockResult, HandleStockResult)
            .Register(messages.PaymentResult, HandlePaymentResult);
    }

    private Guid OrderId => this.GetPrimaryKey();

    private HierarchicalKey OrderKey => HierarchicalKey.Create("orders", OrderId.ToString("N"));

    public Task<OrderDetails?> GetAsync()
    {
        var order = _order.Value;
        var details = order is null
            ? null
            : new OrderDetails(order, _stock.Select(line => new StockLine(line.Key, line.Value)).ToArray());
        return Task.FromResult(details);
    }

    public async Task<bool> CancelAsync()
    {
        if (_order.Value?.Status is not (OrderStatus.Submitted or OrderStatus.AwaitingValidation))
        {
            return false;
        }

        // 待完成的库存预留会在其确认消息到达时释放。
        Cancel("Cancelled by the buyer.");
        await WriteStateAsync();
        return true;
    }

    public async Task<bool> ShipAsync()
    {
        if (_order.Value?.Status is not OrderStatus.Paid)
        {
            return false;
        }

        SetStatus(OrderStatus.Shipped);
        await WriteStateAsync();
        return true;
    }

    private void HandleSubmit(SubmitOrder request, IInboxHandlerContext context)
    {
        if (_order.Value is not null)
        {
            context.Fail($"Order {OrderId} already exists.");
            return;
        }

        var orderDate = request.OrderDate;
        if (orderDate == default)
        {
            // 旧版提交不包含日期。为此保留的私有意图在发送失败及日志恢复后仍然存在。
            if (_submissionDate.Value is not { } captured)
            {
                captured = _timeProvider.GetUtcNow();
                _submissionDate.Value = captured;
            }

            orderDate = captured;
        }

        var order = new OrderState(
            OrderId,
            request.BuyerId,
            Version: 0,
            OrderStatus.Submitted,
            orderDate,
            request.Lines,
            request.Address,
            request.CardLast4,
            Description: null);

        // 收件箱在写入订单提交记录时接受此发送给自身的消息，并在宽限期
        // 结束后处理它。订单日期采用 Durable Jobs 时钟（DeliverAt 也使用该时钟），并在重试之间保持不变。
        var status = CreateStatus(order);
        var gracePeriod = _messages.GracePeriodElapsed.Create(
            new MessageId(OrderKey.CreateChildKey("grace-period")),
            this.GetGrainId(),
            new GracePeriodElapsed(),
            order.OrderDate + _options.GracePeriod);
        _outbox.Send(status);
        _outbox.Send(gracePeriod);
        _order.Value = order;
        context.Complete();
    }

    // 宽限期让买家有时间在预留库存之前取消订单。
    private void HandleGracePeriodElapsed(GracePeriodElapsed message, IInboxHandlerContext context)
    {
        var order = _order.Value!;
        if (order.Status is OrderStatus.Submitted)
        {
            var next = NextStatus(OrderStatus.AwaitingValidation);
            var messages = order.Lines.Select(line => _messages.ReserveStock.Create(
                new MessageId(StockKey(line.ProductId)),
                GrainFactory.GetGrain<ICatalogItemGrain>(line.ProductId).GetGrainId(),
                new ReserveStock(OrderId, line.Units, this.GetGrainId()))).Append(CreateStatus(next)).ToArray();
            foreach (var outgoing in messages)
            {
                _outbox.Send(outgoing);
            }

            foreach (var line in order.Lines) _stock[line.ProductId] = StockLineStatus.Pending;
            _order.Value = next;
        }

        context.Complete();
    }

    private void HandleStockResult(StockResult result, IInboxHandlerContext context)
    {
        var order = _order.Value!;
        if (order.Status is OrderStatus.AwaitingValidation)
        {
            if (!result.Confirmed)
            {
                var productName = order.Lines.First(line => line.ProductId == result.ProductId).ProductName;
                Cancel($"'{productName}' is out of stock.");
                _stock[result.ProductId] = StockLineStatus.Rejected;
            }
            else if (_stock.All(line => line.Key == result.ProductId || line.Value is StockLineStatus.Confirmed))
            {
                var next = NextStatus(OrderStatus.StockConfirmed);
                var payment = _messages.AuthorizePayment.Create(
                    new MessageId(OrderKey.CreateChildKey("payment")),
                    GrainFactory.GetGrain<IPaymentGrain>(OrderId).GetGrainId(),
                    new AuthorizePayment(order.Total, order.CardLast4, this.GetGrainId()));
                var status = CreateStatus(next);
                _outbox.Send(payment);
                _outbox.Send(status);
                _stock[result.ProductId] = StockLineStatus.Confirmed;
                _order.Value = next;
            }
            else
            {
                _stock[result.ProductId] = StockLineStatus.Confirmed;
            }
        }
        else if (order.Status is OrderStatus.Cancelled && result.Confirmed)
        {
            // 此确认消息在取消之后到达。
            _outbox.Send(CreateRelease(result.ProductId));
            _stock[result.ProductId] = StockLineStatus.Released;
        }

        context.Complete();
    }

    private void HandlePaymentResult(PaymentResult result, IInboxHandlerContext context)
    {
        // 库存确认后买家不能取消订单，因此只有处于已确认库存状态的订单等待此结果。去重窗口过后再次投递时，
        // 订单已经越过该阶段。
        if (_order.Value?.Status is not OrderStatus.StockConfirmed)
        {
            context.Complete();
            return;
        }

        if (result.Succeeded)
        {
            var next = NextStatus(OrderStatus.Paid);
            var products = _stock.Keys.ToArray();
            var messages = products.Select(productId => _messages.CommitStock.Create(
                new MessageId(StockKey(productId).CreateChildKey("commit")),
                GrainFactory.GetGrain<ICatalogItemGrain>(productId).GetGrainId(),
                new CommitStock(OrderId))).Append(CreateStatus(next)).ToArray();
            foreach (var outgoing in messages)
            {
                _outbox.Send(outgoing);
            }

            foreach (var productId in products) _stock[productId] = StockLineStatus.Committed;
            _order.Value = next;
        }
        else
        {
            Cancel(result.Reason);
        }

        context.Complete();
    }

    private void Cancel(string? description)
    {
        var next = NextStatus(OrderStatus.Cancelled, description);
        var products = _stock.Where(line => line.Value is StockLineStatus.Confirmed).Select(line => line.Key).ToArray();
        var messages = products.Select(CreateRelease).Append(CreateStatus(next)).ToArray();
        foreach (var outgoing in messages)
        {
            _outbox.Send(outgoing);
        }

        foreach (var productId in products) _stock[productId] = StockLineStatus.Released;
        _order.Value = next;
    }

    private OutboxMessage CreateRelease(int productId) =>
        _messages.ReleaseStock.Create(
            new MessageId(StockKey(productId).CreateChildKey("release")),
            GrainFactory.GetGrain<ICatalogItemGrain>(productId).GetGrainId(),
            new ReleaseStock(OrderId));

    private void SetStatus(OrderStatus status)
    {
        var next = NextStatus(status);
        _outbox.Send(CreateStatus(next));
        _order.Value = next;
    }

    private OrderState NextStatus(OrderStatus status, string? description = null)
    {
        var order = _order.Value!;
        return order with { Status = status, Version = order.Version + 1, Description = description };
    }

    // 不保证投递顺序；买家 Grain 保留版本号最高的摘要。
    private OutboxMessage CreateStatus(OrderState order) =>
        _messages.StatusChanged.Create(
            new MessageId(OrderKey.CreateChildKey("status").CreateChildKey(order.Version.ToString(CultureInfo.InvariantCulture))),
            GrainFactory.GetGrain<IBuyerGrain>(order.BuyerId).GetGrainId(),
            order.ToSummary());

    private HierarchicalKey StockKey(int productId) =>
        OrderKey.CreateChildKey("stock").CreateChildKey(productId.ToString(CultureInfo.InvariantCulture));
}
