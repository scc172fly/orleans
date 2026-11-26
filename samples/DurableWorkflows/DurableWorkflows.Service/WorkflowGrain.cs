using Orleans.Durable.Journaling;
using Orleans.Durable.Tasks;
using Orleans.Runtime;

namespace DurableWorkflows;

// 每种工作流程方法都是一项持久的任务。 呼叫者将它排在根身份识别线下, 如“ 批准- {id} ” 和每个孩子
// 等待那等待它等待的, WithId 根根底下有一个稳定的ID。 silo 失败, 运行时间
// 从一开始就重放方法:完成儿童返回记录的结果,所以工作流程在以下地点恢复
// 停止了,它停下来。
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

    // 以儿童分会/0, 分会/1等形式同时每个项目运行一个活动, 然后返回
    // 每一事务组完成项目顺序的结果。
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

    // 等待审查者的决定直至请求超时结束。截止时间来自工作流程
    // 耐久时钟,所以重播等待相同的最后期限,任务记录收到的第一项决定。
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

    // 在此期间提出的取消请求释放了
    // 保留并记录请求的理由;运行时间一直持续,因此重播也遵循同样的路径。
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
                // 保护儿童运行到完成, 尽管工作流程被取消了 。
                await inventory.ReleaseAsync($"{workflowId}:release").WithId("release").Shielded();
            }

            return new(workflowId, Canceled: true, DurableTask.Current.TaskCancellationReason);
        }
    }

    // 保留库存和收费同时支付,然后是船舶。当一个步骤失败时,那堆子将完全推翻这些步骤
    // 。每个参与者使用操作编号一次,所以重放或重复的剧目不会改变任何变化。
    // 工作流程 grain 是顺序的 grain:请求中列出 grain键所识别的顺序,所以是长
    // 其作用按同一顺序保留。
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

        // WhenAllCompleted 等待两步后返回他们的手柄,
        var prepared = await DurableTask.WhenAllCompleted(
            [inventory.ReserveAsync($"{orderId}:reserve"), payment.ChargeAsync($"{orderId}:charge")])
            .WithId("prepare");
        var (reserve, charge) = (prepared[0], prepared[1]);

        // 结果命名失败的步骤;每个步骤的任务保留诊断的例外。
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
