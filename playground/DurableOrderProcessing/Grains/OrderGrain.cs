using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Orleans.Durable.Tasks;
using Orleans.Durable.Http;
using Orleans.Durable.Journaling;

namespace DurableOrderProcessing.Grains;

public interface IOrderGrain : IGrainWithGuidKey
{
    Task<OrderDetails?> GetAsync();

    // 以流的形式提供自订单提交以来已提交的历史记录，并在订单发货或取消后结束。
    IAsyncEnumerable<OrderDetails> WatchAsync(CancellationToken cancellationToken);

    // 订单工作流，由 POST /api/orders 以操作 orders/{orderId} 的形式启动。
    DurableTask<OrderResult> PlaceOrderAsync(PlaceOrder request);
}

// 遵循 eShop 订单流程的订单流程管理器：
// Submitted（已提交）->（宽限期）-> AwaitingValidation（等待验证）-> StockConfirmed（库存已确认）-> Paid（已支付）-> Shipped（已发货），
// 或在购物车为空、库存预留被拒绝、支付被拒绝或超时，或者买家在
// 支付完成前取消订单时进入 Cancelled（已取消）状态。
// PlaceOrder 是一个持久化任务。Grain 停用后，运行时从头重放该任务：已完成的子任务
// 返回其记录的结果，使工作流从之前停止的位置继续运行。订单状态由其历史记录表示：
// 工作流将当前快照保存在局部变量中，根据请求、记录的结果和
// 逻辑时间计算每次状态转换，并在已记录效果 DurableTask.RunInline 中将新快照追加到历史日志。每个
// 效果及每次买家状态更新都以相应的状态转换命名，因此即使取消和失败路径执行的操作不同，重放也能找到相应记录，
// 并返回记录条目的序列号，而不必将该条目
// 再次追加。订单支付前，买家通过取消该操作来取消订单；发货则通过发送
// 该操作的 shipped 信号完成。
public sealed class OrderGrain : DurableGrain, IOrderGrain
{
    public const string PaymentsClient = "payments";

    // 已支付订单的工作流等待此信号，由 POST /api/orders/operations/{orderId}/signals/shipped 发送。
    public static readonly DurableSignal<Shipment> Shipped = new("shipped");

    private readonly OrderingOptions _options;
    private readonly IDurableHttpClient _payments;
    private readonly IDurableLog<OrderDetails> _history;

    public OrderGrain(IOptions<OrderingOptions> options, [FromKeyedServices(PaymentsClient)] IDurableHttpClient payments)
    {
        _options = options.Value;
        _payments = payments;
        _history = StateManager.GetOrAddLog<OrderDetails>("history");
    }

    private Guid OrderId => this.GetPrimaryKey();

    private OrderDetails? Latest => _history.Count == 0 ? null : _history[_history.NextSequence - 1];

    public Task<OrderDetails?> GetAsync() => Task.FromResult(Latest);

    // 在激活实例上读取历史记录，因此观察者可以在每个快照提交后观察到它。枚举调用
    // 与订单的其他调用交错执行，因此观察者不会延迟工作流。
    public async IAsyncEnumerable<OrderDetails> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var change in _history.ReadAsync(cancellationToken: cancellationToken))
        {
            if (change.Kind is LogEventKind.Sealed)
            {
                yield break;
            }

            if (change is { Kind: LogEventKind.Entry, Item: { } order })
            {
                yield return order;
                if (order.Order.Status is OrderStatus.Shipped or OrderStatus.Cancelled)
                {
                    yield break;
                }
            }
        }
    }

    public async DurableTask<OrderResult> PlaceOrderAsync(PlaceOrder request)
    {
        // 取出购物车中的商品。购物车针对该任务只执行一次此调用，因此重试或重放会读取相同的
        // 明细；结算重试再次到达同一订单时，不会再取走其他商品。
        var lines = await GrainFactory.GetGrain<IBasketGrain>(request.BuyerId).CheckoutAsync(OrderId).WithId("checkout");
        if (lines.Length == 0)
        {
            throw new InvalidOperationException("The basket is empty.");
        }

        var order = OrderDetails.Submit(OrderId, new SubmitOrder(request.BuyerId, lines, request.Address, request.CardLast4), DurableTask.Current.UtcNow);
        DurableTask.RunInline("submitted", () => _history.Append(order));
        Record(order.Order, "submitted");

        // 在订单支付之前，取消其操作会取消此任务。下一次持久化操作观察到
        // 取消请求后抛出 OperationCanceledException，工作流随后释放库存验证可能已经为订单明细
        // 预留的库存。
        OrderLine[] reservable = [];
        try
        {
            await DurableTask.Delay(_options.GracePeriod).WithId("grace-period");
            order = order.BeginValidation();
            reservable = lines;
            DurableTask.RunInline("validating", () => _history.Append(order));
            Record(order.Order, "validating");

            // 并发预留每个订单行，子任务分别为 reserve/0、reserve/1 等。WhenAll 等待每个
            // 库存预留，即使发生取消也是如此；因此该调用返回或抛出异常时，每项明细的预留操作都已结束。
            var confirmed = await lines.Select(line => Catalog(line.ProductId).ReserveAsync(OrderId, line.Units)).WhenAll().WithId("reserve");
            order = order.RecordStock(confirmed);
            if (Array.IndexOf(confirmed, false) is var rejected and >= 0)
            {
                return await CancelOrderAsync(order, lines, $"'{lines[rejected].ProductName}' is out of stock.");
            }

            DurableTask.RunInline("stock-confirmed", () => _history.Append(order));
            Record(order.Order, "stock-confirmed");

            // 取消后，已经完成的库存预留仍然正常返回，因此工作流会在
            // 授权支付前检查是否已请求取消。记录下来的决策使重放沿用首次运行所走的路径。
            if (DurableTask.RunInline("cancellation-check", () => DurableTask.Current.IsTaskCancellationRequested))
            {
                throw new OperationCanceledException();
            }
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            return await CancelOrderAsync(order, reservable, DurableTask.Current.TaskCancellationReason);
        }

        // 在网关处发起支付授权，并以 await-payment/0 轮询该授权，直至授权完成或
        // 超时步骤 await-payment/1 到期。这些步骤发生取消、超时或失败时，工作流放弃
        // 此次支付，并撤销授权，因为授权请求可能已经到达网关。
        DurableHttpOperation? authorization = null;
        PaymentResult? payment = null;
        string? failure = null;
        try
        {
            // 启动步骤受到取消屏蔽保护，因此取消请求不会阻止它完成，工作流由此获知要撤销的授权。
            authorization = await _payments.StartOperationAsync(() => CreateAuthorizationRequest(order, DurableTask.Current.Incarnation)).WithId("authorize-payment").Shielded();
            payment = await _payments.WaitForOperationAsync<PaymentResult>(authorization, new() { PollInterval = _options.PaymentPollInterval })
                .WaitAsync(_options.PaymentTimeout)
                .WithId("await-payment");
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            failure = DurableTask.Current.TaskCancellationReason;
        }
        catch (TimeoutException)
        {
            failure = "Payment authorization timed out.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 其他任何失败，例如网关无法访问、弹性处理器超时或熔断器处于断开状态，或者
            // 响应无法解码，都会导致支付失败。执行尝试被取消时，取消会向外传播，因此下一次执行尝试将重放
            // 工作流。
            failure = "Payment authorization failed.";
        }

        if (payment is not { Succeeded: true })
        {
            if (payment is { Succeeded: false })
            {
                return await CancelOrderAsync(order, lines, payment.Reason);
            }

            failure ??= "The payment gateway returned no result.";
            await VoidPaymentAsync(order, authorization, failure);
            return await CancelOrderAsync(order, lines, failure);
        }

        order = order.MarkPaid();
        DurableTask.RunInline("paid", () => _history.Append(order));
        Record(order.Order, "paid");

        // 已支付的订单将发货：库存确认和等待发货的操作都受到取消屏蔽保护，因此取消不会传播到这些操作。
        // 如果发货信号此前已到达，等待会立即返回。
        await lines.Select(line => Catalog(line.ProductId).CommitAsync(OrderId)).WhenAll().WithId("commit").Shielded();
        await Shipped.WaitAsync().Shielded();
        order = order.MarkShipped();
        DurableTask.RunInline("shipped", () => _history.Append(order));
        Record(order.Order, "shipped");
        return Result(order);
    }

    // 在网关处撤销订单的支付授权，包括订单停止等待后才完成的授权。如果
    // 订单未获知其授权的地址，例如启动请求的响应丢失，则再次使用相同的键发送
    // 授权请求。网关返回已记录的授权；如果此前未记录，则
    // 新建一个授权，然后撤销该授权。请求受到取消屏蔽保护，因此即使订单已取消，仍然会执行。撤销操作
    // 失败时，授权将在网关处过期；无论撤销是否成功，订单都会取消。
    private async Task VoidPaymentAsync(OrderDetails order, DurableHttpOperation? authorization, string reason)
    {
        try
        {
            authorization ??= await _payments.StartOperationAsync(() => CreateAuthorizationRequest(order, DurableTask.Current.Incarnation)).WithId("recover-authorization").Shielded();
            if (authorization.Location is { } location)
            {
                await _payments.CancelOperationAsync(location, reason).WithId("void-payment").Shielded();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    // 创建订单的授权请求。Idempotency-Key 标识订单及工作流的本次运行，因此
    // 本次运行的所有授权请求，包括用于撤销订单尚未获知地址的授权的请求，
    // 都会指向同一授权；在保留期结束后使用订单的工作流 ID 再次运行
    // 则会重新授权。工作流的每个步骤都会报告其运行世代标识。
    private HttpRequestMessage CreateAuthorizationRequest(OrderDetails order, Guid incarnation)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "authorizations")
        {
            Content = JsonContent.Create(new PaymentAuthorizationRequest(OrderId, order.Order.Total, order.Order.CardLast4)),
        };
        request.Headers.Add(PaymentGateway.IdempotencyKeyHeader, $"authorize-{OrderId}-{incarnation:N}");
        return request;
    }

    // 并发释放各订单明细的库存预留，然后记录取消状态。释放操作具有幂等性，并返回
    // 该明细是否持有库存预留，因此工作流可以释放每项明细，而无需记录哪些预留已执行。
    // 释放操作受到取消屏蔽保护，因此导致工作流结束的取消不会阻止此补偿操作运行至完成。
    private async Task<OrderResult> CancelOrderAsync(OrderDetails order, OrderLine[] lines, string? reason)
    {
        var released = await lines.Select(line => Catalog(line.ProductId).ReleaseAsync(OrderId)).WhenAll()
            .WithId("release")
            .Shielded();
        order = order.Cancel([.. lines.Where((_, i) => released[i]).Select(line => line.ProductId)], reason);
        DurableTask.RunInline("cancelled", () => _history.Append(order));
        Record(order.Order, "cancelled");
        return Result(order);
    }

    // 将买家订单投影的更新作为分离任务投递，其根 ID 派生自工作流 ID 和
    // 更新名称，因此工作流无需等待买家 Grain 即可继续，更新独立于
    // 工作流执行。工作流将快照写入订单历史记录后，更新随工作流的下一次日志写入一起提交。
    // 更新可能以任意顺序到达；买家 Grain 保留版本号最高的摘要。
    private void Record(OrderState order, string transition) =>
        _ = GrainFactory.GetGrain<IBuyerGrain>(order.BuyerId).RecordOrderAsync(order.ToSummary())
            .WithId($"record-{transition}")
            .Detached()
            .Post();

    private ICatalogItemGrain Catalog(int productId) => GrainFactory.GetGrain<ICatalogItemGrain>(productId);

    private static OrderResult Result(OrderDetails order) => new(order.Order.Status, order.Order.Description);
}
