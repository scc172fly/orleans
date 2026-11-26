using System.Text.Json.Serialization;
using Orleans.Durable.Messaging;
using Orleans.Serialization;

namespace OrderProcessing;

// 顺序流信息协议:每个约束性对一个主题及其有效载荷类型。
// Grains 输入此单子以发送信件和注册处理器 。
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

// 参数目标识别码保留了现有的定位建构工地铁丝布局。
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

// 信息
[GenerateSerializer, Immutable]
public sealed record SubmitOrder(
    [Id(0)] string BuyerId,
    [Id(1)] OrderLine[] Lines,
    [Id(2)] Address Address,
    [Id(3)] string CardLast4,
    [Id(4)] DateTimeOffset OrderDate);

// 命令是寄给自己 预定在宽限期结束时
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
    // eShop的 GracePeriodManager 等一分钟;较短的默认使降压反应。
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(10);
}

// 日记状态以 JSON 行存储, 所以每个类型都存在需要源生成的元数据。
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
