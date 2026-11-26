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

    /// <summary>获取运行时间所要发送的每个订阅和取消订阅的顺序。</summary>
    public List<RecordedSubscription> Subscriptions { get; } = [];

    /// <summary>获得运行时间在身体中反应所完成的每一个完成,按顺序发送。</summary>
    public List<(RecordedSubscription Subscriber, DurableTaskResponse Response)> Responses { get; } = [];

    /// <summary>获取未找到的运行时间完成的用户发送顺序。</summary>
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

    /// <summary>等待运行时间完成 <paramref name="count"/> 订阅信件的阶段,然后返回最后一个。</summary>
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

/// <summary>订阅、未订阅或未找到的完成,这是运行时阶段的阶段。</summary>
internal sealed record RecordedSubscription(GrainId Sender, GrainId Target, TaskId TaskId, TaskId SubscriberId, bool Unsubscribe)
{
    private static readonly RecordingOutbox Outbox = new();
    private static readonly DurableTaskMessageTransport Encoder = MessagingTestHelpers.CreateTransport(Outbox);

    /// <summary>创建发送此订阅或不订阅目标的收件箱消息。</summary>
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

    /// <summary>创建可传递此记录描述的未找到完成的完整内容的收件箱信件。</summary>
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
