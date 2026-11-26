using Orleans;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Diagnostics;
using Orleans.Durable.Journaling;

namespace Documentation.Grains.DurableTasks.Versioning;

// <durable_tasks_versioning_contracts>
public interface IFulfillmentGrain : IGrainWithStringKey
{
    DurableTask<string> FulfillAsync(string orderId);
}

public interface IWarehouseGrain : IGrainWithStringKey
{
    DurableTask<string> ShipAsync(string orderId);
}

public interface ICarrierGrain : IGrainWithStringKey
{
    DurableTask<string> BookAsync(string orderId);

    DurableTask NotifyAsync(string orderId, string trackingId);
}
// </durable_tasks_versioning_contracts>

public sealed class FulfillmentGrain : DurableGrain, IFulfillmentGrain
{
    private IWarehouseGrain Warehouse => GrainFactory.GetGrain<IWarehouseGrain>("main");

    private ICarrierGrain Carrier => GrainFactory.GetGrain<ICarrierGrain>("main");

    // <durable_tasks_versioning_branch>
    public async DurableTask<string> FulfillAsync(string orderId)
    {
        await Reserve(orderId).WithId("reserve");

        // 版本0是经由仓库发运的代码。版本1载有承运人,版本2也包含承运人
        // 通知客户,等待处决时保留其记录版本。
        var shipping = DurableTask.GetVersion("carrier-booking", minSupported: 0, maxSupported: 2);
        if (shipping >= 1)
        {
            var trackingId = await Carrier.BookAsync(orderId).WithId("book");
            if (shipping >= 2)
            {
                await Carrier.NotifyAsync(orderId, trackingId).WithId("notify");
            }

            return trackingId;
        }

        return await Warehouse.ShipAsync(orderId).WithId("ship");
    }
    // </durable_tasks_versioning_branch>

    private static async DurableTask Reserve(string orderId) => await Task.CompletedTask;
}

public sealed class DescribedFulfillmentGrain : DurableGrain, IFulfillmentGrain
{
    private ICarrierGrain Carrier => GrainFactory.GetGrain<ICarrierGrain>("main");

    private IWarehouseGrain Warehouse => GrainFactory.GetGrain<IWarehouseGrain>("main");

    // <durable_tasks_versioning_descriptor>
    // 一份声明声明更改名称和该代码支持的版本。
    private static readonly DurableVersion CarrierBooking = new("carrier-booking", minSupported: 0, maxSupported: 2);

    public async DurableTask<string> FulfillAsync(string orderId)
    {
        if (!CarrierBooking.IsAtLeast(1))
        {
            return await Warehouse.ShipAsync(orderId).WithId("ship");
        }

        var trackingId = await Carrier.BookAsync(orderId).WithId("book");
        if (CarrierBooking.IsAtLeast(2))
        {
            await Carrier.NotifyAsync(orderId, trackingId).WithId("notify");
        }

        return trackingId;
    }
    // </durable_tasks_versioning_descriptor>
}

public sealed class RetiredFulfillmentGrain : DurableGrain, IFulfillmentGrain
{
    private ICarrierGrain Carrier => GrainFactory.GetGrain<ICarrierGrain>("main");

    // <durable_tasks_versioning_retired>
    public async DurableTask<string> FulfillAsync(string orderId)
    {
        await Reserve(orderId).WithId("reserve");

        // 零版后没有待执行的执行,所以仓库分支已不复存在。
        // 0个在这里拖放,直到支持它代码的代码重播,或者直到操作员中止。
        var shipping = DurableTask.GetVersion("carrier-booking", minSupported: 1, maxSupported: 2);
        var trackingId = await Carrier.BookAsync(orderId).WithId("book");
        if (shipping >= 2)
        {
            await Carrier.NotifyAsync(orderId, trackingId).WithId("notify");
        }

        return trackingId;
    }
    // </durable_tasks_versioning_retired>

    private static async DurableTask Reserve(string orderId) => await Task.CompletedTask;
}

// <durable_tasks_versioning_monitor>
public sealed class VersionMonitor : IObserver<DurableTaskEvents.DurableTaskEvent>, IDisposable
{
    private readonly IDisposable _subscription;
    private readonly Dictionary<(string Name, int Version), long> _recorded = [];

    public VersionMonitor() => _subscription = DurableTaskEvents.AllEvents.Subscribe(this);

    public void OnNext(DurableTaskEvents.DurableTaskEvent value)
    {
        switch (value)
        {
            case DurableTaskEvents.VersionRecorded recorded:
                // 记录旧版本的处决显示它仍在使用中。
                lock (_recorded)
                {
                    _recorded[(recorded.Name, recorded.Version)] = _recorded.GetValueOrDefault((recorded.Name, recorded.Version)) + 1;
                }

                break;
            case DurableTaskEvents.ExecutionStalled stalled:
                // 原因 UnsupportedVersion 或分歧;任务一直等待到代码重放为止。
                Console.WriteLine($"{stalled.GrainContext.GrainId} {stalled.TaskId} stalled ({stalled.Reason}): {stalled.Exception.Message}");
                break;
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void Dispose() => _subscription.Dispose();
}
// </durable_tasks_versioning_monitor>
