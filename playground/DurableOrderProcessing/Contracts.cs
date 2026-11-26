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

/// <summary>保留命令的保留请求和结果,不考虑其持久任务。</summary>
[GenerateSerializer, Immutable]
public sealed record StockReservation(int Units, bool Confirmed);

[GenerateSerializer, Immutable]
public sealed record BasketItem(int ProductId, string ProductName, decimal UnitPrice, int Quantity);

// POST/api/订单的体质,它们检查买方篮子作为新的订单。
// 命令的身份证。
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

// 命令的简略简况:命令工作流程的每一步骤向历史报告的记录。
// 返回下一个快照,作为当前快照及其输入的纯函数,并推进顺序版本。
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

    // 订单确认其库存,但每条线保留时都确认;否则,它的地位
    // 保持不变, 直到工作流程取消它 。
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

// 长期任务参数和结果。运行时将它们保存在 Orleans 连环器
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

// 付款授权的结果,付款网关的状况监测报告。
[GenerateSerializer, Immutable]
public sealed record PaymentResult(bool Succeeded, string? Reason);

// 订单 PlaceOrder 工作流程的结果。
[GenerateSerializer, Immutable]
public sealed record OrderResult(OrderStatus Status, string? Description);

// 订单发送信号的价值,托运人确认其身份。
// 同一批货物成功,重复发出信号,另一批货物与之发生冲突。
[GenerateSerializer, Immutable]
public sealed record Shipment(Guid ShipmentId);

public sealed class OrderingOptions
{
    // eShop的 GracePeriodManager 等一分钟;较短的默认使降压反应。
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(10);

    // 如果付款授权书没有在此期限内完成, 命令将被取消 。
    public TimeSpan PaymentTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // 付款授权的民意测验间隔 不向Retry-after公司做广告
    public TimeSpan PaymentPollInterval { get; set; } = TimeSpan.FromSeconds(1);
}

// 日记状态以 JSON 行存储, 所以每个类型都存在需要源生成的元数据。
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(CatalogItem))]
[JsonSerializable(typeof(StockReservation))]
[JsonSerializable(typeof(BasketItem))]
[JsonSerializable(typeof(OrderDetails))]
[JsonSerializable(typeof(StockLine))]
[JsonSerializable(typeof(OrderSummary))]
internal sealed partial class DurableOrderProcessingJsonContext : JsonSerializerContext;
