namespace Documentation.Diagnostics.DurableTasks;

#pragma warning disable ORLEANS0216
// <orleans0216_violation>
public sealed class CheckoutEndpoint(IOrderWorkflows orders)
{
    // 客户没有耐久发件箱,所以Post投出 InvalidOperationException。
    public void Checkout(Guid orderId) =>
        orders.PlaceOrderAsync(orderId).WithId($"place-order-{orderId:N}").Post();
}
// </orleans0216_violation>
#pragma warning restore ORLEANS0216

// <orleans0216_fix_schedule>
public sealed class ScheduledCheckoutEndpoint(IOrderWorkflows orders)
{
    // 一旦命令 grain 接受并存储任务,返回该返回。使用同一身份的重试将达到相同的任务 。
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

    // 在耐用 grain中,“邮政”将启动在 grain发件箱中的起始阶段,并承诺进行 grain的国家变化。
    public async Task CheckoutAsync(Guid orderId)
    {
        _checkedOut.Value = orderId;
        _ = _orders.PlaceOrderAsync(orderId).WithId($"place-order-{orderId:N}").Post();
        await WriteStateAsync();
    }
}
// </orleans0216_fix_grain>
