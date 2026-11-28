using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;

namespace Documentation.Grains.DurableTasks;

// <durable_tasks_trip_contracts>
[GenerateSerializer, Immutable]
public sealed record TripRequest(string Flight, string Hotel, string Traveler);

[GenerateSerializer, Immutable]
public sealed record TripResult(bool Booked, string? Seat, string? Room, string? Reason);

public interface IFlightGrain : IGrainWithStringKey
{
    DurableTask<string> HoldSeatAsync(Guid tripId);

    DurableTask<bool> ReleaseSeatAsync(Guid tripId);
}

public interface IHotelGrain : IGrainWithStringKey
{
    DurableTask<string> HoldRoomAsync(Guid tripId);

    DurableTask<bool> ReleaseRoomAsync(Guid tripId);
}

public interface IItineraryGrain : IGrainWithStringKey
{
    DurableTask<bool> SendItineraryAsync(Guid tripId, string seat, string room);
}

public interface ITripGrain : IGrainWithGuidKey
{
    DurableTask<TripResult> BookAsync(TripRequest request);
}
// </durable_tasks_trip_contracts>

// <durable_tasks_trip_grain>
public sealed class TripGrain : DurableGrain, ITripGrain
{
    private Guid TripId => this.GetPrimaryKey();

    public async DurableTask<TripResult> BookAsync(TripRequest request)
    {
        var flight = GrainFactory.GetGrain<IFlightGrain>(request.Flight);
        var hotel = GrainFactory.GetGrain<IHotelGrain>(request.Hotel);
        string[] holds;
        try
        {
            // 附属子执行实例：行程任务拥有两个预留操作，取消会传播给它们，WhenAll 仅在
            // 两者都达到终态后返回。整个扇出操作在十分钟后放弃等待，取消其输入并等待它们完成。
            holds = await DurableTask.WhenAll([flight.HoldSeatAsync(TripId), hotel.HoldRoomAsync(TripId)])
                .WaitAsync(TimeSpan.FromMinutes(10))
                .WithId("hold");
        }
        catch (Exception exception) when (exception is TimeoutException || DurableTask.Current.IsTaskCancellationRequested)
        {
            // 即使行程收到取消请求，受取消屏蔽保护的子任务仍会运行至完成，行程仍然
            // 拥有并等待这些子任务。释放从未获得的预留资源会返回 false。
            await DurableTask.WhenAll([flight.ReleaseSeatAsync(TripId), hotel.ReleaseRoomAsync(TripId)])
                .WithId("release")
                .Shielded();
            DurableTask.Current.CancellationToken.ThrowIfCancellationRequested();
            return new TripResult(false, null, null, "The holds weren't confirmed in time.");
        }

        // 分离任务是独立根任务：它的生存期可以超过行程，行程的取消不会传播到它。
        _ = GrainFactory.GetGrain<IItineraryGrain>(request.Traveler)
            .SendItineraryAsync(TripId, holds[0], holds[1])
            .Detached($"itinerary-{TripId:N}")
            .Post();
        return new TripResult(true, holds[0], holds[1], null);
    }
}
// </durable_tasks_trip_grain>

[GenerateSerializer, Immutable]
public sealed record TripPayment(string ChargeId, decimal Amount);

public interface ITripPaymentsGrain : IGrainWithStringKey
{
    DurableTask<bool> RefundAsync(TripPayment payment);
}

public interface ITripCheckoutGrain : IGrainWithGuidKey
{
    DurableTask<TripPayment> AwaitPaymentAsync();
}

public sealed class TripCheckoutGrain : DurableGrain, ITripCheckoutGrain
{
    private static readonly DurableSignal<TripPayment> PaymentReceived = new("payment-received");

    public async DurableTask<TripPayment> AwaitPaymentAsync()
    {
        var payments = GrainFactory.GetGrain<ITripPaymentsGrain>("payments");
        // <durable_tasks_shielded_wait>
        var payment = await PaymentReceived.WaitAsync().Shielded();
        if (DurableTask.Current.IsTaskCancellationRequested)
        {
            await payments.RefundAsync(payment).WithId("refund").Shielded();
        }
        // </durable_tasks_shielded_wait>
        DurableTask.Current.CancellationToken.ThrowIfCancellationRequested();
        return payment;
    }
}

public interface IShippingLabelGrain : IGrainWithStringKey
{
    DurableTask<string> ShipAsync(string orderId);
}

// <durable_tasks_attempt_cancellation>
public sealed class ShippingLabelGrain(
    HttpClient carrier,
    [FromKeyedServices("label")] IDurableValue<string> label) : DurableGrain, IShippingLabelGrain
{
    private readonly SemaphoreSlim _carrierRequests = new(4);

    public async DurableTask<string> ShipAsync(string orderId)
    {
        // 步骤记录标签，因此重放不会再购买一张标签。
        label.Value = await DurableTask.Run(cancellationToken => BuyLabelAsync(orderId, cancellationToken)).WithId("label");
        await DurableTask.Delay(TimeSpan.FromHours(4)).WithId("pickup-window");
        return label.Value;
    }

    private async Task<string> BuyLabelAsync(string orderId, CancellationToken cancellationToken)
    {
        // 普通调用接收任务的 CancellationToken，任务取消和执行尝试取消都会取消此令牌。
        await _carrierRequests.WaitAsync(cancellationToken);
        try
        {
            using var response = await carrier.PostAsync($"labels/{orderId}", content: null, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            // 持久化清理在任务取消时运行。其调用接收执行尝试的令牌，因为任务的
            // 令牌已经取消；如果宿主在清理期间取消执行尝试，这些调用就会迅速结束。
            using var voided = await carrier.DeleteAsync($"labels/{orderId}", DurableTask.Current.AttemptCancellationToken);
            throw;
        }
        finally
        {
            // 进程内清理在两种取消情况下都会运行。
            _carrierRequests.Release();
        }
    }
}
// </durable_tasks_attempt_cancellation>
