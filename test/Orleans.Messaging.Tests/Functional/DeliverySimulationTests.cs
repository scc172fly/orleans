using CsCheck;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Messaging.Tests.Support;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

/// <summary>
/// 运行生成发送、交付故障、碰撞和时间时间表,以一个发件人和一个接收器为对象,并进行检查
/// 端对端交付合同:任何过失都从未两次应用电文的效果,一旦断层停止,每条电文就停止
/// 其绝对重试预算到期时,正完全处理一次有效或发件人未填字母。
/// 围栏终端结果未知:接收者可能已在确认丢失之前应用了电文。
/// </summary>
[Collection(MessagingClusterCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class DeliverySimulationTests : MessagingBehaviorTestBase
{
    private const string Route = "messages/record";
    private static readonly TimeSpan RetryInterval = FaultInjectionClusterFixture.RetryInterval;
    private readonly FaultInjectionClusterFixture _cluster;

    private static readonly Gen<Step> GenStep = Gen.Frequency(
        (4, Gen.Const(new Step(StepKind.Send))),
        (3, Gen.OneOfConst(DeliveryFaultKind.Throw, DeliveryFaultKind.TimeOut, DeliveryFaultKind.LoseResponse, DeliveryFaultKind.TimeOutInFlight)
            .Select(static fault => new Step(StepKind.Fault, fault))),
        (1, Gen.Const(new Step(StepKind.CrashSender))),
        (1, Gen.Const(new Step(StepKind.CrashReceiver))),
        (2, Gen.Const(new Step(StepKind.Advance))));

    public DeliverySimulationTests() : this(new FaultInjectionClusterFixture())
    {
    }

    private DeliverySimulationTests(FaultInjectionClusterFixture cluster) : base(cluster) => _cluster = cluster;

    [Fact]
    public Task GeneratedFaultSchedules_ApplyEachMessageOnceAndDeliverItOnceFaultsStop() =>
        GenStep.Array[1, 12].SampleAsync(RunScheduleAsync, iter: 6, threads: 1);

    private async Task RunScheduleAsync(Step[] schedule)
    {
        var sender = NewGrain();
        var receiver = NewGrain();
        var sent = new List<(MessageId Id, TestMessage Message)>();
        foreach (var step in schedule)
        {
            switch (step.Kind)
            {
                case StepKind.Send:
                    var message = NewMessage(sent.Count, "simulated");
                    sent.Add((await sender.SendAsync(receiver.GetGrainId(), Route, message), message));
                    break;
                case StepKind.Fault:
                    _cluster.Deliveries.Enqueue(Create(step.Fault));
                    break;
                case StepKind.CrashSender:
                    if (Fixture.Cluster.TryGetGrainContext(sender.GetGrainId(), out _)) await _cluster.CrashAsync(sender);
                    break;
                case StepKind.CrashReceiver:
                    if (Fixture.Cluster.TryGetGrainContext(receiver.GetGrainId(), out _)) await _cluster.CrashAsync(receiver);
                    break;
                case StepKind.Advance:
                    _ = await sender.GetSnapshotAsync();
                    await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);
                    break;
            }

            // 故障时的安全屏蔽:电文的效应从未两次应用。
            Assert.All((await receiver.GetSnapshotAsync()).Effects, static effect => Assert.Equal(1, effect.Count));
        }

        // 错误停止; 每一次重试都到期并成功, 除非信件的重试超时先过。
        _cluster.Deliveries.Clear();
        for (var round = 0; round < 8; round++)
        {
            var snapshot = await sender.GetSnapshotAsync();
            if (snapshot.OutboxCount == 0) break;
            await _cluster.AdvanceAndRunPumpAsync(sender, RetryInterval);
            var outbox = Fixture.GetGrainContext(sender).ActivationServices.GetRequiredService<Outbox>();
            await Fixture.WaitUntilAsync(sender, () => !outbox.IsDelivering);
        }

        var final = await sender.GetSnapshotAsync();
        Assert.Equal(0, final.OutboxCount);
        var deadLettered = final.OutboxDeadLetters.Select(static letter => letter.MessageId).ToHashSet();
        var received = await Fixture.WaitForInboxCountAsync(receiver, 0);
        Assert.Empty(received.InboxDeadLetters);
        foreach (var (id, message) in sent)
        {
            var count = received.Effects.Where(effect => effect.LogicalId == message.LogicalId).Sum(static effect => effect.Count);
            if (deadLettered.Contains(id)) Assert.InRange(count, 0, 1);
            else Assert.Equal(1, count);
        }
    }

    private static DeliveryFault Create(DeliveryFaultKind kind) => kind switch
    {
        DeliveryFaultKind.Throw => DeliveryFault.Throw(),
        DeliveryFaultKind.TimeOut => DeliveryFault.TimeOut(),
        DeliveryFaultKind.LoseResponse => DeliveryFault.LoseResponse(),
        _ => DeliveryFault.TimeOutInFlight()
    };

    private enum StepKind { Send, Fault, CrashSender, CrashReceiver, Advance }

    private readonly record struct Step(StepKind Kind, DeliveryFaultKind Fault = default);
}
