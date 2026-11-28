using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Jobs;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging.Tests.Support;
using Orleans.Runtime;
using Orleans.Serialization.Session;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Functional;

[Collection(MessagingClusterCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Messaging")]
public sealed class MessagingActivationBehaviorTests : MessagingBehaviorTestBase
{
    public MessagingActivationBehaviorTests() : base(receiverOnly: false)
    {
    }

    [Fact]
    public async Task ApplicationJournaledStateNamesDoNotCollideWithMessagingState()
    {
        var grain = NewGrain();
        _ = await grain.GetSnapshotAsync();
        var services = Fixture.GetGrainContext(grain).ActivationServices;
        var manager = services.GetRequiredService<IJournaledStateManager>();

        // 测试 Grain 注册名称为 "inbox" 的应用程序状态，即消息传递状态不带前缀的名称。
        Assert.True(manager.TryGetStateMachine("inbox", out var applicationState));
        Assert.IsAssignableFrom<IDurableValue<string>>(applicationState);
        Assert.True(manager.TryGetStateMachine(MessagingStateNames.Inbox, out var inboxState));
        Assert.Same(services.GetRequiredService<Inbox>(), inboxState);
        Assert.NotSame(inboxState, applicationState);
    }
    [Fact]
    public async Task PublicHosting_BindsRealOutboxAndRegisteredMessagingStates()
    {
        var grain = NewGrain();
        _ = await grain.GetSnapshotAsync();
        var services = Fixture.GetGrainContext(grain).ActivationServices;
        var outbox = services.GetRequiredService<Outbox>();
        var inbox = services.GetRequiredService<Inbox>();
        Assert.Same(outbox, services.GetRequiredService<IOutbox>());
        Assert.Same(inbox, services.GetRequiredService<IInbox>());
        var manager = services.GetRequiredService<IJournaledStateManager>();
        Assert.True(manager.TryGetStateMachine(MessagingStateNames.Outbox, out var outboxState));
        Assert.True(manager.TryGetStateMachine(MessagingStateNames.Inbox, out var inboxState));
        Assert.Same(outbox, outboxState);
        Assert.Same(inbox, inboxState);
    }

}
