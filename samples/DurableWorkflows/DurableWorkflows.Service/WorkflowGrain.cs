using Orleans.Durable.Journaling;
using Orleans.Durable.Tasks;
using Orleans.Runtime;

namespace DurableWorkflows;

// 每个工作流方法都是一个持久化任务。调用者使用根 ID（例如 "approval-{id}"）进行调度，通过 WithId 等待的每个子任务
// 都有隶属于该根 ID 的稳定 ID。发生停用或 Silo 故障后，运行时
// 从头重放该方法：已完成的子任务返回记录的结果，因此工作流从
// 先前停止的位置继续运行。
public sealed class WorkflowGrain(ILocalSiloDetails localSiloDetails) : DurableGrain, IWorkflowGrain
{
    private readonly Guid _activationId = Guid.NewGuid();

    private string WorkflowId => this.GetPrimaryKeyString();

    public async DurableTask<BasicWorkflowResult> RunBasicAsync(string input)
    {
        var workflowId = WorkflowId;
        var validated = await Activity($"{workflowId}-validate")
            .ExecuteAsync(new($"{workflowId}:validate", input))
            .WithId("validate");
        var stored = await Activity($"{workflowId}-store")
            .ExecuteAsync(new($"{workflowId}:store", validated.Output))
            .WithId("store");

        return new(workflowId, stored.Output);
    }

    // 为每个项目并发运行一个活动，子任务分别为 branches/0、branches/1 等，并在
    // 每个分支都完成后，按输入项顺序返回结果。
    public async DurableTask<FanOutWorkflowResult> RunFanOutAsync(string[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Length);
        var workflowId = WorkflowId;
        var branches = await DurableTask.WhenAll(items.Select((item, index) =>
                Activity($"{workflowId}-fanout-{index}").ExecuteAsync(new($"{workflowId}:fanout:{index}", item))))
            .WithId("branches");

        return new(workflowId, branches);
    }

    // 等待审核人的决策，直至请求超时。截止时间来自工作流的
    // 持久化时钟，因此重放会等待相同的截止时间，且任务记录接收到的第一个决策。
    public async DurableTask<ApprovalWorkflowResult> RunApprovalAsync(ApprovalRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Subject);
        var deadline = DurableTask.Current.UtcNow + request.Timeout;
        var (received, decision) = await WorkflowSignals.ApprovalDecided.TryWaitAsync(deadline).WithId("decision");
        if (!received)
        {
            decision = new(Approved: false, "No decision arrived before the deadline.");
        }

        var runtime = CreateRuntimeInfo();
        return new(WorkflowId, request.Subject, decision.Approved, decision.Reason, runtime.SiloAddress, runtime.ActivationId);
    }

    // 预留库存并保持至预留期结束。期间收到取消请求时，释放
    // 库存预留并记录请求原因；运行时持久化该请求，使重放走相同的路径。
    public async DurableTask<CancellationWorkflowResult> RunCancellationAsync(TimeSpan holdPeriod)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(holdPeriod, TimeSpan.Zero);
        var workflowId = WorkflowId;
        var inventory = GrainFactory.GetGrain<IInventoryGrain>(workflowId);
        var reservations = await DurableTask.WhenAllCompleted([inventory.ReserveAsync($"{workflowId}:reserve")])
            .WithId("reserve");
        var reservation = reservations[0];
        try
        {
            await reservation;
            await DurableTask.Delay(holdPeriod).WithId("hold");
            return new(workflowId, Canceled: false, Reason: null);
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            if (reservation.Status is DurableTaskStatus.CompletedSuccessfully)
            {
                // 即使工作流已被取消，受取消屏蔽保护的子任务仍会运行至完成。
                await inventory.ReleaseAsync($"{workflowId}:release").WithId("release").Shielded();
            }

            return new(workflowId, Canceled: true, DurableTask.Current.TaskCancellationReason);
        }
    }

    // 并发预留库存和扣款，然后发货。某一步骤失败时，Saga 仅补偿那些
    // 已成功的步骤。每个参与者对相同操作 ID 只应用一次，因此重放或重复执行 Saga 不会再次产生变更。
    // 工作流 Grain 即订单 Grain：请求指定的订单与 Grain 的键标识的订单相同，因此 Saga
    // 及其参与者的副作用记录均保留在同一订单下。
    public async DurableTask<OrderWorkflowResult> RunOrderSagaAsync(OrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var orderId = WorkflowId;
        if (!string.Equals(request.OrderId, orderId, StringComparison.Ordinal))
        {
            throw new ArgumentException($"The order '{request.OrderId}' isn't the order '{orderId}' which this workflow grain runs.", nameof(request));
        }

        var inventory = GrainFactory.GetGrain<IInventoryGrain>(orderId);
        var payment = GrainFactory.GetGrain<IPaymentGrain>(orderId);

        // WhenAllCompleted 等待两个步骤，并返回它们的句柄，每个句柄都包含自己的结果。
        var prepared = await DurableTask.WhenAllCompleted(
            [inventory.ReserveAsync($"{orderId}:reserve"), payment.ChargeAsync($"{orderId}:charge")])
            .WithId("prepare");
        var (reserve, charge) = (prepared[0], prepared[1]);

        // 结果标明失败的步骤；每个步骤的任务都保留其异常以供诊断。
        var failedStep = reserve.Exception is not null ? "reserve" : charge.Exception is not null ? "charge" : null;
        if (failedStep is null)
        {
            try
            {
                await Activity($"{orderId}-shipping")
                    .ExecuteAsync(new($"{orderId}:ship", "ship", request.FailShipping))
                    .WithId("ship");
                return new(orderId, OrderWorkflowStatus.Completed, [], null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failedStep = "ship";
            }
        }

        List<DurableTask<SagaEffect>> compensations = [];
        if (charge.Status is DurableTaskStatus.CompletedSuccessfully)
        {
            compensations.Add(payment.RefundAsync($"{orderId}:refund"));
        }

        if (reserve.Status is DurableTaskStatus.CompletedSuccessfully)
        {
            compensations.Add(inventory.ReleaseAsync($"{orderId}:release"));
        }

        var undone = await DurableTask.WhenAll(compensations).WithId("compensate");
        return new(orderId, OrderWorkflowStatus.Compensated, [.. undone.Select(effect => effect.Kind)], $"The {failedStep} step failed.");
    }

    public Task<WorkflowRuntimeInfo> GetRuntimeInfoAsync() => Task.FromResult(CreateRuntimeInfo());

    private IActivityGrain Activity(string key) => GrainFactory.GetGrain<IActivityGrain>(key);

    private WorkflowRuntimeInfo CreateRuntimeInfo() =>
        new(localSiloDetails.SiloAddress.ToParsableString(), _activationId);
}
