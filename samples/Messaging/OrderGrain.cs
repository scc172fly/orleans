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

    // 请求等待结果。 Grain 代码一次运行一个转, 所以简单的字典就足够了。
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

        // 命令在写入可耐性后即执行。
        await _state.WriteStateAsync();
    }

    public async Task<ReservationOutcome> WaitForOutcomeAsync(MessageId commandId)
    {
        var outcome = _receipts.FirstOrDefault(receipt => receipt.CommandId == commandId);
        if (outcome is null)
        {
            // 等待同一命令的呼唤者 共享一个完成
            if (!_waiters.TryGetValue(commandId, out var waiter))
            {
                waiter = new TaskCompletionSource<ReservationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(commandId, waiter);
            }

            outcome = await waiter.Task;
        }

        // 任何写作都包括答复处理人制作的收据,因此结果一旦返回即为持久。
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
