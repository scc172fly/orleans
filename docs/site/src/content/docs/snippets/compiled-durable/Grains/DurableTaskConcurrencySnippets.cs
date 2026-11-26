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
            // 随随身子女:旅行拥有两个持有,取消后他们就会被扣留, WhenAll 只返回一次
            // 整个扇门在十分钟后就停止了,它的投入被取消并等待。
            holds = await DurableTask.WhenAll([flight.HoldSeatAsync(TripId), hotel.HoldRoomAsync(TripId)])
                .WaitAsync(TimeSpan.FromMinutes(10))
                .WithId("hold");
        }
        catch (Exception exception) when (exception is TimeoutException || DurableTask.Current.IsTaskCancellationRequested)
        {
            // 盾牌儿童虽然要求取消旅行,但即将完成,旅行仍
            // 将自有财产和等待他们。解除一个从未被假借回人所未得的扣留。
            await DurableTask.WhenAll([flight.ReleaseSeatAsync(TripId), hotel.ReleaseRoomAsync(TripId)])
                .WithId("release")
                .Shielded();
            DurableTask.Current.CancellationToken.ThrowIfCancellationRequested();
            return new TripResult(false, null, null, "The holds weren't confirmed in time.");
        }

        // 独立的任务是一个独立的根: 它比旅行长寿, 而取消旅行没有达到它。
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
        // 台阶记录标签,所以重播不会买到另一个
        label.Value = await DurableTask.Run(cancellationToken => BuyLabelAsync(orderId, cancellationToken)).WithId("label");
        await DurableTask.Delay(TimeSpan.FromHours(4)).WithId("pickup-window");
        return label.Value;
    }

    private async Task<string> BuyLabelAsync(string orderId, CancellationToken cancellationToken)
    {
        // 普通电话接收任务 CancellationToken,任务和尝试取消都取消。
        await _carrierRequests.WaitAsync(cancellationToken);
        try
        {
            using var response = await carrier.PostAsync($"labels/{orderId}", content: null, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            // 持久清理运行程序将取消任务。 它的电话收到尝试的标记, 因为任务
            // 已经取消该标记, 如果主机在清理过程中取消尝试, 则会立即终止 。
            using var voided = await carrier.DeleteAsync($"labels/{orderId}", DurableTask.Current.AttemptCancellationToken);
            throw;
        }
        finally
        {
            // 两种取消的工艺中清理运行。
            _carrierRequests.Release();
        }
    }
}
// </durable_tasks_attempt_cancellation>
