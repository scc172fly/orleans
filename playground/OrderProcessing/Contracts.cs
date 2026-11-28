using System.Text.Json.Serialization;
using Orleans.Durable.Messaging;
using Orleans.Serialization;

namespace OrderProcessing;

// 订单流程的消息协议：每个绑定都将主题与其有效载荷类型关联起来。
// Grain 注入此单例来发送消息并注册处理程序。
public sealed class OrderMessages(Serializer serializer)
{
    public MessageType<SubmitOrder> SubmitOrder { get; } = new("ordering.submit-order.v1", serializer);

    public MessageType<GracePeriodElapsed> GracePeriodElapsed { get; } = new("ordering.grace-period-elapsed.v1", serializer);

    public MessageType<OrderSummary> StatusChanged { get; } = new("ordering.status-changed.v1", serializer);

    public MessageType<ReserveStock> ReserveStock { get; } = new("catalog.reserve-stock.v1", serializer);

    public MessageType<StockResult> StockResult { get; } = new("catalog.stock-result.v1", serializer);

    public MessageType<ReleaseStock> ReleaseStock { get; } = new("catalog.release-stock.v1", serializer);

    public MessageType<CommitStock> CommitStock { get; } = new("catalog.commit-stock.v1", serializer);

    public MessageType<AuthorizePayment> AuthorizePayment { get; } = new("payments.authorize.v1", serializer);

    public MessageType<PaymentResult> PaymentResult { get; } = new("payments.result.v1", serializer);
}

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

// 应用于参数的 ID 保留现有构造函数参数与字段之间的按位置对应的传输格式布局。
[GenerateSerializer, Immutable]
public sealed record Address(
    [Id(0)] string Street,
    [Id(1)] string City,
    [Id(2)] string State,
    [Id(3)] string Country,
    [Id(4)] string ZipCode);

[GenerateSerializer, Immutable]
public sealed record OrderLine(
    [Id(0)] int ProductId,
    [Id(1)] string ProductName,
    [Id(2)] decimal UnitPrice,
    [Id(3)] int Units);

[GenerateSerializer, Immutable]
public sealed record CatalogItem(
    [Id(0)] int Id,
    [Id(1)] string Type,
    [Id(2)] string Brand,
    [Id(3)] string Name,
    [Id(4)] decimal Price,
    [Id(5)] int AvailableStock,
    [Id(6)] int ReservedStock,
    [Id(7)] int SoldStock);

[GenerateSerializer, Immutable]
public sealed record BasketItem(
    [Id(0)] int ProductId,
    [Id(1)] string ProductName,
    [Id(2)] decimal UnitPrice,
    [Id(3)] int Quantity);

[GenerateSerializer, Immutable]
public sealed record CheckoutRequest(
    [Id(0)] Guid RequestId,
    [Id(1)] Address Address,
    [Id(2)] string CardNumber);

[GenerateSerializer, Immutable]
public sealed record OrderState(
    [Id(0)] Guid OrderId,
    [Id(1)] string BuyerId,
    [Id(2)] int Version,
    [Id(3)] OrderStatus Status,
    [Id(4)] DateTimeOffset OrderDate,
    [Id(5)] OrderLine[] Lines,
    [Id(6)] Address Address,
    [Id(7)] string CardLast4,
    [Id(8)] string? Description)
{
    public decimal Total => Lines.Sum(line => line.UnitPrice * line.Units);

    public OrderSummary ToSummary() => new(OrderId, Version, Status, Total, OrderDate, Description);
}

[GenerateSerializer, Immutable]
public sealed record StockLine([Id(0)] int ProductId, [Id(1)] StockLineStatus Status);

[GenerateSerializer, Immutable]
public sealed record OrderDetails([Id(0)] OrderState Order, [Id(1)] StockLine[] Stock);

[GenerateSerializer, Immutable]
public sealed record StockReservation([Id(0)] int? Units, [Id(1)] bool Confirmed);

// 消息。
[GenerateSerializer, Immutable]
public sealed record SubmitOrder(
    [Id(0)] string BuyerId,
    [Id(1)] OrderLine[] Lines,
    [Id(2)] Address Address,
    [Id(3)] string CardLast4,
    [Id(4)] DateTimeOffset OrderDate);

// 订单向自身发送此消息，并计划在宽限期结束时投递。
[GenerateSerializer, Immutable]
public sealed record GracePeriodElapsed;

[GenerateSerializer, Immutable]
public sealed record OrderSummary(
    [Id(0)] Guid OrderId,
    [Id(1)] int Version,
    [Id(2)] OrderStatus Status,
    [Id(3)] decimal Total,
    [Id(4)] DateTimeOffset OrderDate,
    [Id(5)] string? Description);

[GenerateSerializer, Immutable]
public sealed record ReserveStock([Id(0)] Guid OrderId, [Id(1)] int Units, [Id(2)] GrainId ReplyTo);

[GenerateSerializer, Immutable]
public sealed record StockResult([Id(0)] int ProductId, [Id(1)] bool Confirmed);

[GenerateSerializer, Immutable]
public sealed record ReleaseStock([Id(0)] Guid OrderId);

[GenerateSerializer, Immutable]
public sealed record CommitStock([Id(0)] Guid OrderId);

[GenerateSerializer, Immutable]
public sealed record AuthorizePayment([Id(0)] decimal Amount, [Id(1)] string CardLast4, [Id(2)] GrainId ReplyTo);

[GenerateSerializer, Immutable]
public sealed record PaymentResult([Id(0)] bool Succeeded, [Id(1)] string? Reason);

public sealed class OrderingOptions
{
    // eShop 的 GracePeriodManager 等待一分钟；此处采用较短的默认时间，以便演示更快响应。
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(10);
}

// 日志化状态以 JSON Lines 格式存储，因此每种持久化类型都需要通过源生成提供元数据。
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DateTimeOffset?))]
[JsonSerializable(typeof(StockLineStatus))]
[JsonSerializable(typeof(CatalogItem))]
[JsonSerializable(typeof(BasketItem))]
[JsonSerializable(typeof(OrderState))]
[JsonSerializable(typeof(OrderSummary))]
[JsonSerializable(typeof(StockReservation))]
internal sealed partial class OrderProcessingJsonContext : JsonSerializerContext;
