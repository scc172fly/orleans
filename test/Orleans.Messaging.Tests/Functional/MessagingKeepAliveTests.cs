using Orleans.Durable.Messaging.Tests.Support;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

[Collection(MessagingClusterCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class MessagingKeepAliveTests : MessagingBehaviorTestBase
{
    private const string HeldRoute = "messages/held";

    [Fact]
    public async Task RunningHandler_KeepsActivationAliveUntilInboxDrains()
    {
        var receiver = NewGrain();
        using var handler = Fixture.HandlerProbe.Arm(receiver.GetGrainId(), HeldRoute);
        var envelope = CreateEnvelope(receiver, NewMessage(1, "held"), HeldRoute);
        DeliveryResultAssert.IsCase<DeliveryResult.Accepted>(await DeliverAsync(receiver, envelope));
        await handler.WaitUntilEnteredAsync();
        var context = Fixture.GetGrainContext(receiver);

        // 处理程序在任何 Grain 请求之外运行，因此只有消息泵持有的保活引用能阻止空闲回收。
        var management = Fixture.Client.GetGrain<IManagementGrain>(0);
        await management.ForceActivationCollection(TimeSpan.Zero, TestContext.Current.CancellationToken);
        Assert.False(context.Deactivated.IsCompleted);

        handler.Release();
        Assert.Equal("held", Assert.Single((await Fixture.WaitForEffectCountAsync(receiver, 1)).Effects).Value);
        await Fixture.CollectAsync(context);
    }
}
