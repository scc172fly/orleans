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

    // 将命令的从提交时起就流传到历史中,一旦命令船舶或取消,即告终止。
    IAsyncEnumerable<OrderDetails> WatchAsync(CancellationToken cancellationToken);

    // 命令工作流程,即POST/api/ords以操作命令({orderId}开始的流程。
    DurableTask<OrderResult> PlaceOrderAsync(PlaceOrder request);
}

// 命令处理管理器, 在 eShop的订单流后 :
// 提交 - > (宽限期) - > AwaitingValidation - > StockConfirmed - > 已支付 - > 已装运,
// 或当篮子空空、库存被拒、付款被拒绝或退出或买方取消时取消
// 在命令支付之前
// PlaceOrder 是一个耐久的任务。 停止后, 运行时间会从开始重弹: 完成儿童任务
// 命令状态是它的历史:
// 工作流程将当前快照保存在本地变量中, 从请求中计算每次过渡, 记录结果,
// 和逻辑时间,并将新的快照附加到历史日志中,以记录效果 DurableTask.RunInline。
// 并指定每买方更新效果, 以其过渡为名称, 因此重放会发现记录, 尽管取消和
// 失败路径运行不同的操作,返回被记录的条目的序列而不附加
// 买方再次取消订单,在支付之前取消其经营,并发送
// 操作的发运信号
public sealed class OrderGrain : DurableGrain, IOrderGrain
{
    public const string PaymentsClient = "payments";

    // 支付订单的工作流程等待这一信号,即POST/api/命令/操作/{orderId}/信号/发货发送的信号。
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

    // 读取激活的历史, 所以观察者观察每个快照一旦发生。
    // 与订单的其他电话互连 所以观察者不会延迟工作流程
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
        // 篮子的物品。篮子运行了一次 要求完成这项任务, 所以重试或重放读取相同
        // 线条,以及重新审阅的退票到达的命令不再需要其他项目。
        var lines = await GrainFactory.GetGrain<IBasketGrain>(request.BuyerId).CheckoutAsync(OrderId).WithId("checkout");
        if (lines.Length == 0)
        {
            throw new InvalidOperationException("The basket is empty.");
        }

        var order = OrderDetails.Submit(OrderId, new SubmitOrder(request.BuyerId, lines, request.Address, request.CardLast4), DurableTask.Current.UtcNow);
        DurableTask.RunInline("submitted", () => _history.Append(order));
        Record(order.Order, "submitted");

        // 直至命令被支付之前,取消其运作将取消这一任务。
        // 并丢弃 OperationCanceledException,工作流程释放了审定可能使用的行
        // 已经保留了。
        OrderLine[] reservable = [];
        try
        {
            await DurableTask.Delay(_options.GracePeriod).WithId("grace-period");
            order = order.BeginValidation();
            reservable = lines;
            DurableTask.RunInline("validating", () => _history.Append(order));
            Record(order.Order, "validating");

            // 保留每一行,作为儿童储备/0、储备/1等。 WhenAll 等待时间
            // 保留,包括取消后的保留,因此每一行的保留在返回或抛出时即予解决。
            var confirmed = await lines.Select(line => Catalog(line.ProductId).ReserveAsync(OrderId, line.Units)).WhenAll().WithId("reserve");
            order = order.RecordStock(confirmed);
            if (Array.IndexOf(confirmed, false) is var rejected and >= 0)
            {
                return await CancelOrderAsync(order, lines, $"'{lines[rejected].ProductName}' is out of stock.");
            }

            DurableTask.RunInline("stock-confirmed", () => _history.Append(order));
            Record(order.Order, "stock-confirmed");

            // 通常在取消后完成返回的保留意见保留,因此工作流程要检查之前的
            // 授权付款。所记录的决定在第一次运行的路径上保留重播。
            if (DurableTask.RunInline("cancellation-check", () => DurableTask.Current.IsTaskCancellationRequested))
            {
                throw new OperationCanceledException();
            }
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            return await CancelOrderAsync(order, reservable, DurableTask.Current.TaskCancellationReason);
        }

        // 授权在网关支付,并调查核准额,作为等待付款/0,直至完成或
        // 取消、暂停或这些步骤的失败都放弃了
        // 支付,因为批准申请可能到达网关,工作流程将取消支付。
        DurableHttpOperation? authorization = null;
        PaymentResult? payment = null;
        string? failure = null;
        try
        {
            // 启动被屏蔽, 所以取消会让它完成, 而工作流程会学习无效授权 。
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
            // 任何其他故障,如无法进入的网关、复原力处理器超时或开路线路,或
            // 取消的尝试会传播,所以下一次尝试重放
            // 工作流程。
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

        // 定船,行军和等待发运的船 都蒙蔽了屏蔽,所以取消不会到达他们手中
        // 等待货物的返回时间是较早之前运到的。
        await lines.Select(line => Catalog(line.ProductId).CommitAsync(OrderId)).WhenAll().WithId("commit").Shielded();
        await Shipped.WaitAsync().Shielded();
        order = order.MarkShipped();
        DurableTask.RunInline("shipped", () => _history.Append(order));
        Record(order.Order, "shipped");
        return Result(order);
    }

    // 命令在网关上公布,包括命令停止等待后完成的授权。
    // 命令中未得知授权方的 批准位置,例如当启动响应丢失时,发送
    // 键盘再次请求授权,网关以其记录的授权进行回复,或
    // 并且将一个记录下来,而那(记录其中的律例)无效。请求是被遮蔽的,所以他们可以运行,尽管命令被撤销。
    // 无法在网关上让授权到期,命令将被取消。
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

    // 创建命令授权请求。 它的“ 电子资产- Key ” 指定命令和工作流程运行, 所以每个
    // 申请竞选许可,包括吊销命令所在地点授权的许可
    // 保留了之后 命令工作流程的身份证
    // 工作流程的每个步骤都报告工作流程的化身。
    private HttpRequestMessage CreateAuthorizationRequest(OrderDetails order, Guid incarnation)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "authorizations")
        {
            Content = JsonContent.Create(new PaymentAuthorizationRequest(OrderId, order.Order.Total, order.Order.CardLast4)),
        };
        request.Headers.Add(PaymentGateway.IdempotencyKeyHeader, $"authorize-{OrderId}-{incarnation:N}");
        return request;
    }

    // 解脱线条同时释放保留, 然后记录取消。 解除是无能的返回
    // 这条线是否保留,因此工作流程释放每一条线,而不跟踪保留是哪一条。
    // 解封后会保护好, 所以结束工作流程的取消会让补偿工作顺利完成。
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

    // 将买方预测更新作为单独任务,其根ID来自工作流程的ID和
    // 更新的名称, 因此工作流程会继续而不等待买家, 更新运行独立于
    // 工作流程。更新承诺在工作流程记录快照后,以工作流程下一期日记写方式进行
    // 更新以任何订单方式送达;买方将摘要保存在最高版本。
    private void Record(OrderState order, string transition) =>
        _ = GrainFactory.GetGrain<IBuyerGrain>(order.BuyerId).RecordOrderAsync(order.ToSummary())
            .WithId($"record-{transition}")
            .Detached()
            .Post();

    private ICatalogItemGrain Catalog(int productId) => GrainFactory.GetGrain<ICatalogItemGrain>(productId);

    private static OrderResult Result(OrderDetails order) => new(order.Order.Status, order.Order.Description);
}
