namespace Documentation.Diagnostics.DurableTasks;

public sealed class DeliveryGrain : DurableGrain
{
#pragma warning disable ORLEANS0218
    // <orleans0218_violation>
    public async DurableTask<string> DeliverAsync(Guid orderId, string region, int newestVersion)
    {
        // 名称取决于区域，版本范围取决于调用方。
        var version = DurableTask.GetVersion($"carrier-booking-{region}", 0, newestVersion);
        return version >= 1
            ? await Book(orderId, region).WithId("book")
            : await Ship(orderId, region).WithId("ship");
    }
    // </orleans0218_violation>
#pragma warning restore ORLEANS0218

    // <orleans0218_fix_constant>
    private const string CarrierBookingChange = "carrier-booking";

    public async DurableTask<string> DeliverByCarrierAsync(Guid orderId, string region)
    {
        // 每次重放都读取执行实例以此名称记录的版本。
        var version = DurableTask.GetVersion(CarrierBookingChange, minSupported: 0, maxSupported: 1);
        return version >= 1
            ? await Book(orderId, region).WithId("book")
            : await Ship(orderId, region).WithId("ship");
    }
    // </orleans0218_fix_constant>

    // <orleans0218_fix_descriptor>
    private static readonly DurableVersion CarrierBooking = new("carrier-booking", minSupported: 0, maxSupported: 1);

    public async DurableTask<string> DeliverWithDescriptorAsync(Guid orderId, string region) =>
        CarrierBooking.IsAtLeast(1)
            ? await Book(orderId, region).WithId("book")
            : await Ship(orderId, region).WithId("ship");
    // </orleans0218_fix_descriptor>

    private static async DurableTask<string> Book(Guid orderId, string region) => $"{region}/booked/{orderId:N}";

    private static async DurableTask<string> Ship(Guid orderId, string region) => $"{region}/shipped/{orderId:N}";
}
