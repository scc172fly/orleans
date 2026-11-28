namespace Documentation.Diagnostics.DurableTasks;

#pragma warning disable ORLEANS0216
// <orleans0216_violation>
public sealed class CheckoutEndpoint(IOrderWorkflows orders)
{
    // 客户端没有持久化发件箱，因此 Post 抛出 InvalidOperationException。
    public void Checkout(Guid orderId) =>
        orders.PlaceOrderAsync(orderId).WithId($"place-order-{orderId:N}").Post();
}
// </orleans0216_violation>
#pragma warning restore ORLEANS0216

// <orleans0216_fix_schedule>
public sealed class ScheduledCheckoutEndpoint(IOrderWorkflows orders)
{
    // 订单所属 Grain 接受并存储任务后即返回。使用相同 ID 重试会到达同一任务。
    public async Task<string> CheckoutAsync(Guid orderId)
    {
        var workflow = await orders.PlaceOrderAsync(orderId).WithId($"place-order-{orderId:N}").ScheduleAsync();
        return workflow.Id.ToString();
    }
}
// </orleans0216_fix_schedule>

// <orleans0216_fix_grain>
public sealed class BasketGrain : DurableGrain
{
    private readonly IOrderWorkflows _orders;
    private readonly IDurableValue<Guid?> _checkedOut;

    public BasketGrain(IOrderWorkflows orders)
    {
        _orders = orders;
        _checkedOut = StateManager.GetOrAddValue<Guid?>("checked-out");
    }

    // 在持久化 Grain 中，Post 将启动操作暂存到 Grain 的发件箱中，并与 Grain 状态变更一同提交。
    public async Task CheckoutAsync(Guid orderId)
    {
        _checkedOut.Value = orderId;
        _ = _orders.PlaceOrderAsync(orderId).WithId($"place-order-{orderId:N}").Post();
        await WriteStateAsync();
    }
}
// </orleans0216_fix_grain>
