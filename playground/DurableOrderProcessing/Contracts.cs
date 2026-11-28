using System.Text.Json.Serialization;

namespace DurableOrderProcessing;

public enum OrderStatus
{
    Submitted,
    AwaitingValidation,
    StockConfirmed,
    Paid,
    Shipped,
    Cancelled,
}

public enum StockLineStatus
{
    Pending,
    Confirmed,
    Rejected,
    Released,
    Committed,
}

[GenerateSerializer, Immutable]
public sealed record Address(string Street, string City, string State, string Country, string ZipCode);

[GenerateSerializer, Immutable]
public sealed record OrderLine(int ProductId, string ProductName, decimal UnitPrice, int Units);

[GenerateSerializer, Immutable]
public sealed record CatalogItem(
    int Id,
    string Type,
    string Brand,
    string Name,
    decimal Price,
    int AvailableStock,
    int ReservedStock,
    int SoldStock);

/// <summary>独立于订单的持久化任务，保留该订单的库存预留请求及其结果。</summary>
[GenerateSerializer, Immutable]
public sealed record StockReservation(int Units, bool Confirmed);

[GenerateSerializer, Immutable]
public sealed record BasketItem(int ProductId, string ProductName, decimal UnitPrice, int Quantity);

// POST /api/orders 的请求体，将买家的购物车结算为新订单。请求的 Idempotency-Key 是
// 订单 ID。
public sealed record CheckoutRequest(string BuyerId, Address Address, string CardNumber);

[GenerateSerializer, Immutable]
public sealed record OrderState(
    Guid OrderId,
    string BuyerId,
    int Version,
    OrderStatus Status,
    DateTimeOffset OrderDate,
    OrderLine[] Lines,
    Address Address,
    string CardLast4,
    string? Description)
{
    public decimal Total => Lines.Sum(line => line.UnitPrice * line.Units);

    public OrderSummary ToSummary() => new(OrderId, Version, Status, Total, OrderDate, Description);
}

[GenerateSerializer, Immutable]
public sealed record StockLine(int ProductId, StockLineStatus Status);

// 订单快照：订单工作流的每个步骤都将其作为条目写入历史记录。每次状态转换
// 都将当前快照及其输入作为纯函数的参数，返回下一个快照，并递增订单版本。
[GenerateSerializer, Immutable]
public sealed record OrderDetails(OrderState Order, StockLine[] Stock)
{
    public static OrderDetails Submit(Guid orderId, SubmitOrder request, DateTimeOffset orderDate) => new(
        new OrderState(
            orderId,
            request.BuyerId,
            Version: 0,
            OrderStatus.Submitted,
            orderDate,
            request.Lines,
            request.Address,
            request.CardLast4,
            Description: null),
        []);

    public OrderDetails BeginValidation() =>
        Next(OrderStatus.AwaitingValidation, [.. Order.Lines.Select(line => new StockLine(line.ProductId, StockLineStatus.Pending))]);

    // 记录每个订单明细的库存预留情况。只有全部明细均成功预留时，订单才确认库存；否则其状态
    // 保持不变，直到工作流取消订单。
    public OrderDetails RecordStock(bool[] confirmed)
    {
        StockLine[] stock = [.. Order.Lines.Select((line, i) =>
            new StockLine(line.ProductId, confirmed[i] ? StockLineStatus.Confirmed : StockLineStatus.Rejected))];
        return Array.IndexOf(confirmed, false) < 0 ? Next(OrderStatus.StockConfirmed, stock) : this with { Stock = stock };
    }

    public OrderDetails MarkPaid() =>
        Next(OrderStatus.Paid, [.. Stock.Select(line => line with { Status = StockLineStatus.Committed })]);

    public OrderDetails MarkShipped() => Next(OrderStatus.Shipped, Stock);

    public OrderDetails Cancel(int[] released, string? reason) => Next(
        OrderStatus.Cancelled,
        [.. Stock.Select(line => released.Contains(line.ProductId) ? line with { Status = StockLineStatus.Released } : line)],
        reason);

    private OrderDetails Next(OrderStatus status, StockLine[] stock, string? description = null) =>
        new(Order with { Status = status, Version = Order.Version + 1, Description = description }, stock);
}

// 持久化任务的参数和结果。运行时使用 Orleans 序列化器存储这些数据。
[GenerateSerializer, Immutable]
public sealed record PlaceOrder(string BuyerId, Address Address, string CardLast4);

[GenerateSerializer, Immutable]
public sealed record SubmitOrder(string BuyerId, OrderLine[] Lines, Address Address, string CardLast4);

[GenerateSerializer, Immutable]
public sealed record OrderSummary(
    Guid OrderId,
    int Version,
    OrderStatus Status,
    decimal Total,
    DateTimeOffset OrderDate,
    string? Description);

// 支付网关的状态监视器报告的支付授权结果。
[GenerateSerializer, Immutable]
public sealed record PaymentResult(bool Succeeded, string? Reason);

// 订单 PlaceOrder 工作流的结果。
[GenerateSerializer, Immutable]
public sealed record OrderResult(OrderStatus Status, string? Description);

// 订单发货信号的值，由发货方标识。工作流记录首次发货，因此
// 重复发送相同发货信息的信号会成功，而不同的发货信息会发生冲突。
[GenerateSerializer, Immutable]
public sealed record Shipment(Guid ShipmentId);

public sealed class OrderingOptions
{
    // eShop 的 GracePeriodManager 等待一分钟；此处采用较短的默认时间，以便演示更快响应。
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(10);

    // 如果支付授权未在此时间内完成，则取消订单。
    public TimeSpan PaymentTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // 对未通过 Retry-After 指明轮询间隔的支付授权，采用此轮询间隔。
    public TimeSpan PaymentPollInterval { get; set; } = TimeSpan.FromSeconds(1);
}

// 日志化状态以 JSON Lines 格式存储，因此每种持久化类型都需要通过源生成提供元数据。
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(CatalogItem))]
[JsonSerializable(typeof(StockReservation))]
[JsonSerializable(typeof(BasketItem))]
[JsonSerializable(typeof(OrderDetails))]
[JsonSerializable(typeof(StockLine))]
[JsonSerializable(typeof(OrderSummary))]
internal sealed partial class DurableOrderProcessingJsonContext : JsonSerializerContext;
