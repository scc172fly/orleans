using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Orleans.Concurrency;
using Orleans.Configuration;
using Orleans.Internal;
using Orleans.Metadata;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.TestingHost;
using Orleans.TestingHost.Utils;
using Tester.CodeGenTests;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// Orleans 托管客户端功能的测试。
    /// 托管客户端是与 Silo 在同一进程中运行的 Orleans 客户端，
    /// 使应用程序代码与 Grain 能在进程内通信。
    /// 此模式在托管 Orleans 的 ASP.NET Core 应用程序中很常见。
    /// </summary>
    [TestCategory("BVT"), TestCategory("HostedClient")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class HostedClientTests : IClassFixture<HostedClientTests.Fixture>
    {
        private readonly TimeSpan _timeout = Debugger.IsAttached ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(10);
        private readonly IHost _host;
        private readonly FakeTimeProvider _messagingTimeProvider;

        public class Fixture : IAsyncLifetime
        {
            private readonly TestClusterPortAllocator portAllocator;
            public IHost Host { get; private set; } = null!;
            public FakeTimeProvider MessagingTimeProvider { get; } = new();

            public Fixture()
            {
                portAllocator = new TestClusterPortAllocator();
            }

            public async ValueTask InitializeAsync()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                var (siloPort, gatewayPort) = portAllocator.AllocateConsecutivePortPairs(1);
                var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
                builder.UseOrleans(siloBuilder =>
                {
                    siloBuilder
                        .UseLocalhostClustering(siloPort, gatewayPort)
                        .Configure<ClusterOptions>(options =>
                        {
                            options.ClusterId = Guid.NewGuid().ToString();
                            options.ServiceId = Guid.NewGuid().ToString();
                        })
                        .ConfigureLogging(logging => logging.AddDebug())
                        .AddMemoryGrainStorage("PubSubStore")
                        .AddMemoryStreams<DefaultMemoryMessageBodySerializer>("MemStream");
                });
                builder.Services.AddKeyedSingleton<TimeProvider>(TimeProviderNames.Messaging, MessagingTimeProvider);
                Host = builder.Build();
                await Host.StartAsync(cancellationToken);
            }

            public async ValueTask DisposeAsync()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                try
                {
                    await Host.StopAsync(cancellationToken);
                }
                finally
                {
                    Host.Dispose();
                    portAllocator.Dispose();
                }
            }
        }

        public HostedClientTests(Fixture fixture)
        {
            _host = fixture.Host;
            _messagingTimeProvider = fixture.MessagingTimeProvider;
        }

        /// <summary>
        /// 测试从托管客户端发起的基本 Grain 调用。
        /// 验证进程内客户端能与同一进程中运行的 Grain 成功通信，
        /// 展示托管客户端的核心模式。
        /// </summary>
        [Fact]
        public async Task HostedClient_GrainCallTest()
        {
            var client = _host.Services.GetRequiredService<IClusterClient>();

            var grain = client.GetGrain<ISimpleGrain>(65);
            await grain.SetA(23);
            var val = await grain.GetA();
            Assert.Equal(23, val);
        }

        /// <summary>
        /// 测试托管客户端 Grain 调用的超时行为。
        /// 验证响应超时得到正确执行，且
        /// 运行时在超时场景中能正确跟踪正在运行的请求。
        /// 这确保 Grain 无响应时的可靠性。
        /// </summary>
        [Fact]
        public async Task HostedClient_TimeoutTest()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var client = _host.Services.GetRequiredService<IClusterClient>();
            var runtimeClient = _host.Services.GetRequiredService<IRuntimeClient>();
            var typeResolver = _host.Services.GetRequiredService<GrainInterfaceTypeResolver>();

            var stuckGrainType = typeResolver.GetGrainInterfaceType(typeof(IStuckGrain));
            var initialTimeout = runtimeClient.GetResponseTimeout();

            var timeout = TimeSpan.FromSeconds(1);

            try
            {
                runtimeClient.SetResponseTimeout(timeout);
                var assertionTask = Assert.ThrowsAsync<TimeoutException>(
                    async () =>
                    {
                        var grain = client.GetGrain<IStuckGrain>(Guid.NewGuid());
                        await grain.RunForever();
                    });

                Assert.False(assertionTask.IsCompleted);
                Assert.Equal(expected: 1, actual: runtimeClient.GetRunningRequestsCount(stuckGrainType));

                // 至少每秒检查一次回调是否过期。
                _messagingTimeProvider.Advance(timeout + TimeSpan.FromSeconds(1));
                await assertionTask.WaitAsync(cancellationToken);

                Assert.Equal(expected: 0, actual: runtimeClient.GetRunningRequestsCount(stuckGrainType));
            }
            finally
            {
                runtimeClient.SetResponseTimeout(initialTimeout);
            }
        }

        /// <summary>
        /// 测试经由 Grain 调用传递的对象的引用相等性语义。
        /// 验证不可变对象（字符串、Grain 引用）保持引用相等性，
        /// 而可变对象（数组）被复制。同时展示 Immutable&lt;T&gt; 包装器，
        /// 用于在需要时保留可变对象的引用相等性。
        /// </summary>
        [Fact]
        public async Task HostedClient_ReferenceEquality_GrainCallTest()
        {
            var client = _host.Services.GetRequiredService<IClusterClient>();
            var grain = client.GetGrain<IGrainWithGenericMethods>(Guid.NewGuid());

            // 字符串是不可变的。
            object expected = new string('*', 5);
            var actual = await grain.RoundTrip(expected);
            Assert.Same(expected, actual);

            // Grain 引用是不可变的。
            actual = await grain.RoundTrip(grain);
            Assert.Same(grain, actual);

            // 数组不是不可变的，因此预期会发生复制。
            var collection = new int[] { 1, 3, 9 };
            actual = await grain.RoundTrip(collection);
            Assert.NotSame(expected, actual);

            // Immutable<T> 应在不进行任何复制的情况下完成往返。
            var expectedImmutable = new Immutable<int[]>(collection);
            var actualImmutable = await grain.RoundTrip(expectedImmutable);
            Assert.Same(expectedImmutable.Value, actualImmutable.Value);
        }

        /// <summary>
        /// 测试托管客户端的观察者模式。
        /// 展示客户端如何注册观察者对象，以接收
        /// 来自 Grain 的通知，从而支持基于推送的通信模式。
        /// 验证回调在正确的上下文中得到正确调用。
        /// </summary>
        [Fact]
        public async Task HostedClient_ObserverTest()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var client = _host.Services.GetRequiredService<IClusterClient>();

            var handle = new AsyncResultHandle();

            var callbackCounter = new int[1];
            var callbacksReceived = new bool[2];

            var grain = client.GetGrain<ISimpleObserverableGrain>(0);
            var observer = new ObserverTests.SimpleGrainObserver(
                (a, b, result) =>
                {
                    Assert.Null(RuntimeContext.Current);
                    callbackCounter[0]++;

                    if (a == 3 && b == 0)
                        callbacksReceived[0] = true;
                    else if (a == 3 && b == 2)
                        callbacksReceived[1] = true;
                    else
                        throw new ArgumentOutOfRangeException("Unexpected callback with values: a=" + a + ",b=" + b);

                    if (callbackCounter[0] == 1)
                    {
                        // 允许回调按任意顺序发生
                        Assert.True(callbacksReceived[0] || callbacksReceived[1]);
                    }
                    else if (callbackCounter[0] == 2)
                    {
                        Assert.True(callbacksReceived[0] && callbacksReceived[1]);
                        result.Done = true;
                    }
                    else
                    {
                        Assert.True(false);
                    }
                },
                handle,
                client.ServiceProvider.GetRequiredService<ILogger<ISimpleGrainObserver>>());
            var reference = client.CreateObjectReference<ISimpleGrainObserver>(observer);
            await grain.Subscribe(reference);
            await grain.SetA(3);
            await grain.SetB(2);

            Assert.True(await handle.WaitForFinished(_timeout).WaitAsync(cancellationToken));

            client.DeleteObjectReference<ISimpleGrainObserver>(reference);
            Assert.NotNull(observer);
        }

        /// <summary>
        /// 测试托管客户端的流功能。
        /// 验证客户端可以订阅 Orleans 流，并接收
        /// 发布到这些流的消息，展示用于事件驱动通信模式的
        /// 流抽象。
        /// </summary>
        [Fact]
        public async Task HostedClient_StreamTest()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var client = _host.Services.GetRequiredService<IClusterClient>();

            var handle = new AsyncResultHandle();
            var vals = new List<int>();
            var stream0 = client.GetStreamProvider("MemStream").GetStream<int>("hi", Guid.Empty);
            await stream0.SubscribeAsync(
                (val, token) =>
                {
                    vals.Add(val);
                    if (vals.Count >= 2) handle.Done = true;
                    return Task.CompletedTask;
                });
            var stream = client.GetStreamProvider("MemStream").GetStream<int>("hi", Guid.Empty);
            await stream.OnNextAsync(1);
            await stream.OnNextAsync(409);
            Assert.True(await handle.WaitForFinished(_timeout).WaitAsync(cancellationToken));
            Assert.Equal(new[] { 1, 409 }, vals);
        }
    }
}
