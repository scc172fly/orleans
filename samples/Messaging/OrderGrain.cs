using Orleans;
using Orleans.Durable.Messaging;
using Orleans.Durable.Journaling;
using Orleans.Runtime;

namespace Messaging;

public interface IOrderGrain : IGrainWithStringKey
{
    Task ReserveAsync(GrainId stock, MessageId commandId, int quantity);
    Task<ReservationOutcome> WaitForOutcomeAsync(MessageId commandId);
}

public sealed class OrderGrain : Grain, IOrderGrain
{
    private readonly IInbox _inbox;
    private readonly IOutbox _outbox;
    private readonly IDurableStateManager _state;
    private readonly StockMessages _messages;
    private readonly IDurableList<ReservationOutcome> _receipts;

    // 等待结果的请求。Grain 代码一次只执行一个轮次，因此普通字典就足够了。
    private readonly Dictionary<MessageId, TaskCompletionSource<ReservationOutcome>> _waiters = [];

    public OrderGrain(IInbox inbox, IOutbox outbox, IDurableStateManager state, StockMessages messages)
    {
        _inbox = inbox;
        _outbox = outbox;
        _state = state;
        _messages = messages;
        _receipts = state.GetOrAddState<IDurableList<ReservationOutcome>>("receipts");
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _inbox.Register(_messages.Result, HandleResult);
        return base.OnActivateAsync(cancellationToken);
    }

    public async Task ReserveAsync(GrainId stock, MessageId commandId, int quantity)
    {
        _outbox.Send(_messages.Reserve, commandId, stock, new ReserveStock(quantity, this.GetGrainId()));

        // 本次写入完成持久化后，命令即可投递。
        await _state.WriteStateAsync();
    }

    public async Task<ReservationOutcome> WaitForOutcomeAsync(MessageId commandId)
    {
        var outcome = _receipts.FirstOrDefault(receipt => receipt.CommandId == commandId);
        if (outcome is null)
        {
            // 等待同一命令的并发调用者共享一个完成对象。
            if (!_waiters.TryGetValue(commandId, out var waiter))
            {
                waiter = new TaskCompletionSource<ReservationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(commandId, waiter);
            }

            outcome = await waiter.Task;
        }

        // 任何写入都会包含回复处理程序暂存的回执，因此写入返回时，结果已持久化。
        await _state.WriteStateAsync();
        return outcome;
    }

    private void HandleResult(ReservationOutcome outcome, IInboxHandlerContext context)
    {
        if (context.Message.Envelope.MessageId != new MessageId(outcome.CommandId.Key.CreateChildKey("result")))
        {
            context.Fail("The reply doesn't identify the reservation command it answers.");
            return;
        }

        _receipts.Add(outcome);
        context.Complete();
        if (_waiters.Remove(outcome.CommandId, out var waiter)) waiter.SetResult(outcome);
    }
}
