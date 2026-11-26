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
    /// Orleans主机客户端功能测试。
    /// 主机客户端是 Orleans 客户端, 与 silo运行在同一程序内,
    /// 启用应用程序代码与 grains之间的进程内通信。
    /// 这种模式在 ASP.NET 核心应用程序托管 Orleans.
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
        /// 向主机客户进行基本 grain 调用测试。
        /// 证明在处理过程中的客户端能够成功与 grains进行通信
        /// 在同一过程中运行,展示核心托管客户端模式。
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
        /// 测试主机客户端调用 grain 的超时行为。
        /// 证明反应超时执行得当,而且
        /// 运行时间正确跟踪在超时情况下运行请求的轨迹。
        /// 这在 grains 变得不反应时确保可靠性。
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

                // 返回到期时至少每秒检查一次。
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
        /// 测试通过 grain 调用对象的平等语义。
        /// 证明不可变物体(字符串、 grain 参考)保持参考平等,
        /// 复制可变对象(数组)时。请同时显示不可变<T> 包装器
        /// 以在必要时维护可变物的参考平等。
        /// </summary>
        [Fact]
        public async Task HostedClient_ReferenceEquality_GrainCallTest()
        {
            var client = _host.Services.GetRequiredService<IClusterClient>();
            var grain = client.GetGrain<IGrainWithGenericMethods>(Guid.NewGuid());

            // 弦是不可改变的
            object expected = new string('*', 5);
            var actual = await grain.RoundTrip(expected);
            Assert.Same(expected, actual);

            // Grain 引用是不可更改的。
            actual = await grain.RoundTrip(grain);
            Assert.Same(grain, actual);

            // 阵列并非不可改变, 所以要复制一份 。
            var collection = new int[] { 1, 3, 9 };
            actual = await grain.RoundTrip(collection);
            Assert.NotSame(expected, actual);

            // 不可更改的<T> 应绕行,不得复制。
            var expectedImmutable = new Immutable<int[]>(collection);
            var actualImmutable = await grain.RoundTrip(expectedImmutable);
            Assert.Same(expectedImmutable.Value, actualImmutable.Value);
        }

        /// <summary>
        /// 从托管客户测试观察者模式 。
        /// 证明客户如何登记观察员收到的物品
        /// grains通知,有利于基于推力的通信模式。
        /// 证明在正确的背景下适当援引回调。
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
                        // 允许按任何顺序进行回调
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
        /// 从主机客户端测试流功能 。
        /// 证明客户可以订阅 Orleans 流并接收
        /// 向这些流发布信息,演示流
        /// 由事件驱动的通信模式的抽象。
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
