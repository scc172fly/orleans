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

// 命令处理管理器, 在 eShop的订单流后 :
// 提交 - > (宽限期) - > AwaitingValidation - > StockConfirmed - > 已支付 - > 已装运,
// 或当股票被拒、付款被拒或买方在确认存货之前取消时予以注销。
// 每一个转型都改变 日记状态和阶段 发出的信息在一原子杂志的写作。
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

        // 待确认的保留意见在确认后予以解除。
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
            // 其私人意图在发送和日记追偿失败后得以维持。
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

        // 收件箱接受此自上信件, 以顺序提交文件写入并处理它 一旦宽度
        // 订单日期在使用 DeliverAt 的持久工作时钟上,并固定在每轮中。
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

    // 宽限期给予买方在保留存货前取消销售的时间。
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
            // 取消后即确认。
            _outbox.Send(CreateRelease(result.ProductId));
            _stock[result.ProductId] = StockLineStatus.Released;
        }

        context.Complete();
    }

    private void HandlePaymentResult(PaymentResult result, IInboxHandlerContext context)
    {
        // 股票一旦确认,买方不能取消购买者,因此只有经确认的订单等待这一结果。
        // 重复窗口发现已经过顺序的窗口。
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

    // 交货订单未具体说明;买方将摘要保存在最高版本。
    private OutboxMessage CreateStatus(OrderState order) =>
        _messages.StatusChanged.Create(
            new MessageId(OrderKey.CreateChildKey("status").CreateChildKey(order.Version.ToString(CultureInfo.InvariantCulture))),
            GrainFactory.GetGrain<IBuyerGrain>(order.BuyerId).GetGrainId(),
            order.ToSummary());

    private HierarchicalKey StockKey(int productId) =>
        OrderKey.CreateChildKey("stock").CreateChildKey(productId.ToString(CultureInfo.InvariantCulture));
}
