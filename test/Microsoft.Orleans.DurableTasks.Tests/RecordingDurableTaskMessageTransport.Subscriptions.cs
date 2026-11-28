#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Messaging;
using Orleans.Runtime;

namespace Microsoft.Orleans.DurableTasks.Tests;

internal sealed partial class RecordingDurableTaskMessageTransport
{
    private readonly List<(int Count, TaskCompletionSource<RecordedSubscription> Waiter)> _subscriptionWaiters = [];

    /// <summary>按发送顺序获取运行时暂存的所有订阅和取消订阅消息。</summary>
    public List<RecordedSubscription> Subscriptions { get; } = [];

    /// <summary>按发送顺序获取运行时暂存的所有在消息体内携带响应的完成消息。</summary>
    public List<(RecordedSubscription Subscriber, DurableTaskResponse Response)> Responses { get; } = [];

    /// <summary>按发送顺序获取运行时以未找到任务的结果通知完成的订阅者。</summary>
    public List<RecordedSubscription> NotFound =>
        [.. Responses.Where(static entry => entry.Response.Exception is DurableTaskNotFoundException).Select(static entry => entry.Subscriber)];

    public void SendResponse(GrainId sender, DurableTaskSubscriber destination, TaskId taskId, DurableTaskResponse response)
    {
        Responses.Add((new(sender, destination.GrainId, taskId, destination.TaskId, Unsubscribe: false), response));
        StateChanges.Notify();
    }

    public void SendSubscription(GrainId sender, GrainId target, TaskId taskId, TaskId subscriberId) =>
        AddSubscription(new(sender, target, taskId, subscriberId, Unsubscribe: false));

    public void SendUnsubscription(GrainId sender, GrainId target, TaskId taskId, TaskId subscriberId) =>
        AddSubscription(new(sender, target, taskId, subscriberId, Unsubscribe: true));

    private void AddSubscription(RecordedSubscription subscription)
    {
        Subscriptions.Add(subscription);
        foreach (var (count, waiter) in _subscriptionWaiters)
        {
            if (Subscriptions.Count >= count)
            {
                waiter.TrySetResult(Subscriptions[count - 1]);
            }
        }

        StateChanges.Notify();
    }

    /// <summary>等待运行时暂存 <paramref name="count"/> 条订阅消息，并返回最后一条。</summary>
    public Task<RecordedSubscription> WaitForSubscriptionAsync(int count)
    {
        if (Subscriptions.Count >= count)
        {
            return Task.FromResult(Subscriptions[count - 1]);
        }

        var waiter = new TaskCompletionSource<RecordedSubscription>(TaskCreationOptions.RunContinuationsAsynchronously);
        _subscriptionWaiters.Add((count, waiter));
        return waiter.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}

/// <summary>运行时暂存的订阅消息、取消订阅消息或未找到任务的完成消息。</summary>
internal sealed record RecordedSubscription(GrainId Sender, GrainId Target, TaskId TaskId, TaskId SubscriberId, bool Unsubscribe)
{
    private static readonly RecordingOutbox Outbox = new();
    private static readonly DurableTaskMessageTransport Encoder = MessagingTestHelpers.CreateTransport(Outbox);

    /// <summary>创建将此订阅或取消订阅投递至其目标的收件箱消息。</summary>
    public InboxMessage ToInboxMessage() => Encode(transport =>
    {
        if (Unsubscribe)
        {
            transport.SendUnsubscription(Sender, Target, TaskId, SubscriberId);
        }
        else
        {
            transport.SendSubscription(Sender, Target, TaskId, SubscriberId);
        }
    });

    /// <summary>创建用于投递此记录所描述的未找到任务的完成消息的收件箱消息。</summary>
    public InboxMessage ToNotFoundInboxMessage() =>
        Encode(transport => transport.SendResponse(
            Sender,
            new(Target, SubscriberId),
            TaskId,
            DurableTaskResponse.FromException(new DurableTaskNotFoundException(TaskId))));

    private static InboxMessage Encode(Action<DurableTaskMessageTransport> send)
    {
        lock (Outbox)
        {
            var count = Outbox.Messages.Count;
            send(Encoder);
            return Outbox.Messages[count].ToInboxMessage();
        }
    }
}
