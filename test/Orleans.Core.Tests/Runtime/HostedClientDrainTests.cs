using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Orleans;
using Orleans.CodeGeneration;
using Orleans.Configuration;
using Orleans.GrainReferences;
using Orleans.Metadata;
using Orleans.Placement.Repartitioning;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;
using Orleans.Serialization;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.Serializers;
using TestExtensions;
using Xunit;

namespace UnitTests.Runtime;

[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Runtime")]
[TestCategory("BVT")]
public class HostedClientDrainTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ReceiveStatusResponse_DisposesMessageAndPreservesPendingCallback(bool knownRequest, bool malformed)
    {
        var fixture = new HostedFixture(nameof(ReceiveStatusResponse_DisposesMessageAndPreservesPendingCallback));
        var callbacks = ReadCallbacks(fixture.Runtime);
        var completion = new CallbackCompletionSource();
        using var request = new Message
        {
            Direction = Message.Directions.Request,
            Id = new CorrelationId(2051),
            SendingGrain = fixture.Hosted.GrainId
        };
        var callback = new CallbackData(
            new SharedCallbackData(
                message => callbacks.TryRemove((message.SendingGrain, message.Id), out _),
                NullLogger<CallbackData>.Instance,
                fixture.Clock,
                TimeSpan.FromMinutes(1),
                cancelOnTimeout: false,
                waitForCancellationAcknowledgement: false,
                cancellationManager: null,
                fixture.ApplicationRequests),
            completion,
            request);
        try
        {
            using var response = new BufferedResponse(fixture.Runtime.ServiceProvider, request.Id, request.SendingGrain, malformed);
            if (knownRequest)
            {
                Assert.True(callbacks.TryAdd((request.SendingGrain, request.Id), callback));
            }

            var error = Record.Exception(() => fixture.Runtime.ReceiveResponse(response.Message));
            Assert.Equal(malformed, error is InvalidCastException);
            if (!malformed)
            {
                Assert.Null(error);
            }

            Assert.Null(response.Message._bodyObject);
            Assert.Equal(0, response.Request.Body.Length);
            Assert.False(callback.IsCompleted);
            Assert.Equal(0, completion.CompletionCount);
            Assert.Equal(knownRequest, callbacks.ContainsKey((request.SendingGrain, request.Id)));
            if (knownRequest)
            {
                callback.OnTimeout();
                var timeout = Assert.IsType<TimeoutException>(completion.Exception);
                Assert.Equal(!malformed, timeout.Message.Contains("processing", StringComparison.Ordinal));
                Assert.Equal(1, completion.CompletionCount);
                Assert.Empty(callbacks);
            }
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceiveCacheInvalidationRejection_DisposesMessageAndPreservesPendingCallback(bool knownRequest)
    {
        var fixture = new HostedFixture(nameof(ReceiveCacheInvalidationRejection_DisposesMessageAndPreservesPendingCallback));
        var callbacks = ReadCallbacks(fixture.Runtime);
        var completion = new CallbackCompletionSource();
        using var request = new Message
        {
            Direction = Message.Directions.Request,
            Id = new CorrelationId(2052),
            SendingGrain = fixture.Hosted.GrainId
        };
        var callback = new CallbackData(
            new SharedCallbackData(
                message => callbacks.TryRemove((message.SendingGrain, message.Id), out _),
                NullLogger<CallbackData>.Instance,
                fixture.Clock,
                TimeSpan.FromMinutes(1),
                cancelOnTimeout: false,
                waitForCancellationAcknowledgement: false,
                cancellationManager: null,
                fixture.ApplicationRequests),
            completion,
            request);
        try
        {
            using var response = new BufferedResponse(fixture.Runtime.ServiceProvider, new Message
            {
                Direction = Message.Directions.Response,
                Result = Message.ResponseTypes.Rejection,
                Id = request.Id,
                TargetGrain = request.SendingGrain,
                TargetSilo = fixture.Address,
                BodyObject = new RejectionResponse { RejectionType = Message.RejectionTypes.CacheInvalidation },
                CacheInvalidationHeader = [new GrainAddressCacheUpdate(
                    new GrainAddress { GrainId = GrainId.Create("test", "invalid"), SiloAddress = fixture.Address },
                    validAddress: null)]
            });
            if (knownRequest)
            {
                Assert.True(callbacks.TryAdd((request.SendingGrain, request.Id), callback));
            }

            fixture.Runtime.ReceiveResponse(response.Message);

            Assert.Null(response.Message._bodyObject);
            Assert.Equal(0, response.Request.Body.Length);
            Assert.False(callback.IsCompleted);
            Assert.Equal(0, completion.CompletionCount);
            Assert.Equal(knownRequest, callbacks.ContainsKey((request.SendingGrain, request.Id)));
            if (knownRequest)
            {
                Assert.Same(callback, callbacks[(request.SendingGrain, request.Id)]);
                callback.OnTimeout();
                Assert.Equal(1, completion.CompletionCount);
                Assert.Empty(callbacks);
            }
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task OnStop_DrainsChannelAdmittedUndispatchedMessages()
    {
        var fixture = new HostedFixture(nameof(OnStop_DrainsChannelAdmittedUndispatchedMessages));
        var first = fixture.Track(new ClassifierHeldInvokable(1601, fixture.DescribeState, fixture.Events));
        var second = fixture.Track(new ScopeCheckingInvokable(1602, fixture.DescribeState, fixture.Events));
        try
        {
            await fixture.StartAsync();
            fixture.Deliver(first);
            await fixture.WaitAsync(first.ClassifierEntered.Task, "1601: second GetInterfaceType entered on hosted pump");
            Assert.Equal(2, first.ClassifierCalls);
            Assert.Equal(0, first.InvocationCount);

            // 唯一的读取者在管理器准入之前被阻塞。此调用返回可以证明
            // 消息 1602 已获准进入通道，而不是仅被测试调度器排入队列。
            fixture.Deliver(second);
            Assert.Equal(0, second.InvocationCount);
            var stop = fixture.Stop(TestContext.Current.CancellationToken);
            Assert.False(stop.IsCompleted, fixture.DescribeState());

            first.ClassifierRelease.TrySetResult();
            await fixture.WaitAsync(first.ScopeEntered.Task, "1601: invocation entered after classifier release");
            Assert.Equal(0, second.InvocationCount);
            first.Release.TrySetResult();
            await fixture.WaitAsync(second.ScopeEntered.Task, "1602: channel-admitted invocation entered");
            Assert.True(first.Exited.Task.IsCompletedSuccessfully, fixture.DescribeState());
            Assert.False(second.Exited.Task.IsCompleted, fixture.DescribeState());
            Assert.False(stop.IsCompleted, fixture.DescribeState());

            second.Release.TrySetResult();
            await fixture.WaitAsync(stop, "hosted pump and both manager admissions drained");
            fixture.AssertExecuted(first, second);
            Assert.Equal(
                new[] { "1601:entered", "1601:exiting", "1602:entered", "1602:exiting" },
                fixture.Events.ToArray());
            Assert.Equal(3, first.ClassifierCalls);
            fixture.AssertScopeLive();
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task ReceiveMessage_CompletesOutstandingResponseDuringDrain()
    {
        var fixture = new HostedFixture(nameof(ReceiveMessage_CompletesOutstandingResponseDuringDrain));
        var completion = new CallbackCompletionSource();
        var invocation = fixture.Track(new CallbackAwaitingScopeInvokable(1701, fixture.DescribeState, fixture.Events, completion));
        CallbackData? callback = null;
        var outgoing = new Message
        {
            Direction = Message.Directions.Request,
            Id = new CorrelationId(1702),
            SendingGrain = fixture.Hosted.GrainId,
            TargetGrain = fixture.Sender,
        };
        var key = (outgoing.SendingGrain, outgoing.Id);
        var succeeded = false;
        try
        {
            var callbacks = ReadCallbacks(fixture.Runtime);
            var shared = new SharedCallbackData(
                message => callbacks.TryRemove((message.SendingGrain, message.Id), out _),
                NullLogger<CallbackData>.Instance,
                fixture.Clock,
                TimeSpan.FromMinutes(1),
                cancelOnTimeout: false,
                waitForCancellationAcknowledgement: false,
                cancellationManager: Substitute.For<IGrainCallCancellationManager>(),
                fixture.ApplicationRequests);
            callback = new CallbackData(shared, completion, outgoing);
            Assert.True(callbacks.TryAdd(key, callback), fixture.DescribeState());
            Assert.False(callback.IsCompleted);
            Assert.Equal(0, completion.CompletionCount);

            await fixture.StartAsync();
            fixture.Deliver(invocation);
            await fixture.WaitAsync(invocation.ScopeEntered.Task, "1701: observer held before response");
            await fixture.WaitAsync(invocation.CallbackAwaiting.Task, "1701: observer awaiting tracked callback 1702");
            var stop = fixture.Stop(TestContext.Current.CancellationToken);
            Assert.False(stop.IsCompleted, fixture.DescribeState());
            Assert.False(invocation.Exited.Task.IsCompleted, fixture.DescribeState());

            // 模拟已发出的出站调用，无需 SendRequest 或路由。成功响应
            // 经过 InsideRuntimeClient 仅处理回调的分支：不涉及目录或传输层。
            using var responseBody = Response.FromResult(731);
            var response = new Message
            {
                Direction = Message.Directions.Response,
                Result = Message.ResponseTypes.Success,
                Id = outgoing.Id,
                TargetGrain = outgoing.SendingGrain,
                SendingGrain = outgoing.TargetGrain,
                BodyObject = responseBody,
            };
            fixture.Hosted.ReceiveMessage(response);
            await fixture.WaitAsync(
                completion.Completed.Task,
                "1702: real callback completed during observer drain",
                () => $"callbackCount={completion.CompletionCount}; ledgerContainsKey={callbacks.ContainsKey(key)}");
            Assert.Equal(731, Assert.IsType<int>(completion.Result));
            Assert.Null(completion.Exception);
            Assert.Equal(1, completion.CompletionCount);
            Assert.True(callback.IsCompleted);
            Assert.False(callbacks.ContainsKey(key));
            Assert.False(invocation.Release.Task.IsCompleted);
            await fixture.WaitAsync(stop, "1701: awaited response releases actual hosted drain");
            Assert.Equal(731, Assert.IsType<int>(invocation.CallbackResult));
            fixture.AssertExecuted(invocation);

            // 重复响应不得再次完成原来的 Promise。
            fixture.Hosted.ReceiveMessage(response);
            Assert.Equal(1, completion.CompletionCount);
            Assert.False(callbacks.ContainsKey(key));
            fixture.AssertScopeLive();

            Assert.Equal(1, completion.CompletionCount);
            succeeded = true;
        }
        finally
        {
            // 仅在断言或阶段失败后执行应急解阻。它无法证明上述任何
            // 成功路径的完成断言成立，而且绝不会进入传输路径。
            try
            {
                if (!succeeded && callback is { IsCompleted: false })
                {
                    callback.OnHostShutdown();
                }
            }
            finally
            {
                await fixture.CleanupAsync();
            }
        }
    }

    [Fact]
    public async Task OnStop_CancellationControlBypassesClosedChannel()
    {
        var fixture = new HostedFixture(nameof(OnStop_CancellationControlBypassesClosedChannel));
        var invocation = fixture.Track(new CancellableScopeInvokable(1751, fixture.DescribeState, fixture.Events));
        var control = new CancellationControlInvokable(fixture.Sender, new CorrelationId(invocation.Id));
        control.Release.TrySetResult();
        try
        {
            await fixture.StartAsync();
            fixture.Deliver(invocation);
            await fixture.WaitAsync(invocation.ScopeEntered.Task, "1751: cancellable observer held");
            var stop = fixture.Stop(TestContext.Current.CancellationToken); // 同步关闭通道。
            Assert.False(stop.IsCompleted, fixture.DescribeState());
            Assert.Equal(0, invocation.CancelCount);

            // 此操作必须绕过现已关闭的应用程序通道，到达实际的
            // LocalObjectData 取消扩展，而非仅进入交错执行的测试调用主体。
            fixture.Hosted.ReceiveMessage(new Message
            {
                Id = new CorrelationId(1752),
                TargetGrain = fixture.ObserverId,
                SendingGrain = fixture.Sender,
                Direction = Message.Directions.OneWay,
                IsAlwaysInterleave = true,
                BodyObject = control,
            });
            await fixture.WaitAsync(control.CancellationSent.Task, "1752: cancellation passed closed channel");
            await fixture.WaitAsync(stop, "1751: observer exited because hosted control canceled it");
            Assert.Equal(1, control.InvocationCount);
            Assert.True(control.Exited.Task.IsCompletedSuccessfully);
            Assert.Same(invocation.TargetHolder, control.CancellationTarget);
            Assert.Equal(1, invocation.CancelCount);
            Assert.True(((IInvokable)invocation).GetCancellationToken().IsCancellationRequested);
            fixture.AssertExecuted(invocation);
            fixture.AssertScopeLive();
        }
        finally
        {
            // 成功路径中不由测试解除阻塞：只有 CancelRequest 才会解除此调用主体的阻塞。
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task OnStop_CancellationDefersScopeDisposalUntilDrain()
    {
        var fixture = new HostedFixture(nameof(OnStop_CancellationDefersScopeDisposalUntilDrain));
        var invocation = fixture.Track(new ScopeCheckingInvokable(1801, fixture.DescribeState, fixture.Events));
        using var stopCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        try
        {
            await fixture.StartAsync();
            fixture.Deliver(invocation);
            await fixture.WaitAsync(invocation.ScopeEntered.Task, "1801: observer using hosted scope");
            var canceledStop = fixture.Stop(stopCancellation.Token);
            Assert.False(canceledStop.IsCompleted, fixture.DescribeState());
            stopCancellation.Cancel();
            await fixture.WaitAsync(canceledStop, "canceled OnStop returned normally");
            Assert.True(canceledStop.IsCompletedSuccessfully, fixture.DescribeState());
            fixture.AssertScopeLive();
            Assert.False(invocation.Exited.Task.IsCompleted, fixture.DescribeState());

            // 已取消的生命周期等待返回后，宿主清理可以请求 Dispose。
            await fixture.DisposeOnWorkerAsync("Dispose after canceled OnStop returned");
            fixture.AssertScopeLive();
            var drain = fixture.Stop(TestContext.Current.CancellationToken);
            Assert.False(drain.IsCompleted, fixture.DescribeState());
            Assert.False(invocation.Release.Task.IsCompleted);

            invocation.Release.TrySetResult();
            await fixture.WaitAsync(drain, "1801: uncanceled hosted drain");
            await fixture.WaitAsync(fixture.Scopes.ScopeDisposed.Task, "owned scope disposed after canceled stop");
            fixture.AssertExecuted(invocation);
            fixture.AssertDisposedAfterExecution();
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task Dispose_ReturnsWithoutDisposingScopeDuringDrain()
    {
        var fixture = new HostedFixture(nameof(Dispose_ReturnsWithoutDisposingScopeDuringDrain));
        var invocation = fixture.Track(new ScopeCheckingInvokable(1901, fixture.DescribeState, fixture.Events));
        try
        {
            await fixture.StartAsync();
            fixture.Deliver(invocation);
            await fixture.WaitAsync(invocation.ScopeEntered.Task, "1901: observer held before direct Dispose");
            Assert.Equal(0, fixture.StopCalls);

            // 每个工作线程仅在同步 Dispose 调用返回之后发出信号。若 Dispose 阻塞，
            // 有时限的握手将失败；任务调度或经过的时间并不是正确性判据。
            await fixture.DisposeOnWorkerAsync("first direct Dispose returned");
            await fixture.DisposeOnWorkerAsync("repeated direct Dispose returned");
            Assert.Equal(2, fixture.DisposeReturns);
            Assert.Equal(0, fixture.StopCalls);
            Assert.False(invocation.Release.Task.IsCompleted);
            Assert.False(invocation.Exited.Task.IsCompleted, fixture.DescribeState());
            fixture.AssertScopeLive();

            var drain = fixture.Stop(TestContext.Current.CancellationToken);
            Assert.False(drain.IsCompleted, fixture.DescribeState());
            invocation.Release.TrySetResult();
            await fixture.WaitAsync(drain, "1901: actual drain initiated by direct Dispose");
            await fixture.WaitAsync(fixture.Scopes.ScopeDisposed.Task, "scope disposal following direct Dispose");
            fixture.AssertExecuted(invocation);
            fixture.AssertDisposedAfterExecution();
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task Dispose_DisposesOwnedScopeExactlyOnceAcrossRepeatedCalls()
    {
        var fixture = new HostedFixture(nameof(Dispose_DisposesOwnedScopeExactlyOnceAcrossRepeatedCalls));
        var invocation = fixture.Track(new ScopeCheckingInvokable(2001, fixture.DescribeState, fixture.Events));
        try
        {
            Assert.Equal(1, fixture.Scopes.CreateCount);
            await fixture.StartAsync();
            fixture.Deliver(invocation);
            await fixture.WaitAsync(invocation.ScopeEntered.Task, "2001: admitted scope owner");
            await fixture.DisposeOnWorkerAsync("ownership: first Dispose returned");
            await fixture.DisposeOnWorkerAsync("ownership: second Dispose returned during drain");
            await fixture.DisposeOnWorkerAsync("ownership: third Dispose returned during drain");
            Assert.Equal(3, fixture.DisposeReturns);
            Assert.False(invocation.Exited.Task.IsCompleted, fixture.DescribeState());
            fixture.AssertScopeLive();

            var drain = fixture.Stop(TestContext.Current.CancellationToken);
            Assert.False(drain.IsCompleted, fixture.DescribeState());
            invocation.Release.TrySetResult();
            await fixture.WaitAsync(drain, "2001: actual drain before ownership assertions");
            await fixture.WaitAsync(fixture.Scopes.ScopeDisposed.Task, "ownership: first scope disposal completed");
            fixture.AssertExecuted(invocation);
            fixture.AssertDisposedAfterExecution();

            // 不要仅在首次后台释放信号之后采样计数。实际排空现已完成，
            // 未加保护的 Dispose 会在这些握手之前再次同步调用包装器。
            // 底层 DI 的幂等性不能掩盖这些调用。
            await fixture.DisposeOnWorkerAsync("ownership: fourth Dispose returned after completed drain");
            await fixture.DisposeOnWorkerAsync("ownership: fifth Dispose returned after completed drain");
            Assert.Equal(5, fixture.DisposeReturns);
            fixture.AssertDisposedAfterExecution();
            Assert.Equal(
                new[] { "2001:entered", "2001:exiting", "scope-disposing", "marker-disposed", "scope-disposed" },
                fixture.Events.ToArray());
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

    private static ConcurrentDictionary<(GrainId, CorrelationId), CallbackData> ReadCallbacks(InsideRuntimeClient runtime)
    {
        // 唯一使用反射的接入点：读取现有台账，绝不替换它，也不检查托管
        // 通道、管理器、生命周期或释放状态。这用于模拟尚未完成的出站调用。
        var field = typeof(InsideRuntimeClient).GetField("callbacks", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(runtime) is not ConcurrentDictionary<(GrainId, CorrelationId), CallbackData> callbacks)
        {
            throw new InvalidOperationException(
                $"{nameof(ReceiveMessage_CompletesOutstandingResponseDuringDrain)} requires " +
                "InsideRuntimeClient.callbacks to be ConcurrentDictionary<(GrainId, CorrelationId), CallbackData>.");
        }

        return callbacks;
    }

    private sealed class HostedFixture
    {
        private readonly string _test;
        private readonly ServiceProvider _root;
        private readonly DrainObserver _observer = new();
        private readonly List<ScopeCheckingInvokable> _invocations = [];
        private readonly List<Task> _workers = [];
        private readonly ConcurrentQueue<Task> _stops = new();
        private readonly ILifecycleObserver _lifecycleObserver;
        private readonly IDisposable _subscription = Substitute.For<IDisposable>();
        private int _disposeReturns;

        internal HostedFixture(string test)
        {
            _test = test;
            var services = new ServiceCollection();
            services.AddSerializer();
            services.AddLogging();
            services.AddMetrics();
            services.AddScoped(_ => new ScopeMarker(Events));
            _root = services.BuildServiceProvider();

            Scopes = new ScopeTrackingProvider(
                _root,
                () => _invocations.All(invocation => invocation.Exited.Task.IsCompletedSuccessfully),
                Events);
            var deepCopier = _root.GetRequiredService<DeepCopier>();
            var instruments = new OrleansInstruments(_root.GetRequiredService<IMeterFactory>());
            ApplicationRequests = new ApplicationRequestInstruments(instruments);
            var messaging = new MessagingInstruments(instruments);
            var processing = new MessagingProcessingInstruments(instruments);
            var loggerFactory = NullLoggerFactory.Instance;
            var trace = new MessagingTrace(loggerFactory, messaging, processing);
            var messageFactory = new MessageFactory(deepCopier, NullLogger<MessageFactory>.Instance, trace);
            var options = Options.Create(new SiloMessagingOptions());
            var mapping = new InterfaceToImplementationMappingCache();
            var referenceRuntime = Substitute.For<IGrainReferenceRuntime>();
            var referenceActivator = new GrainReferenceActivator(
                Scopes, [new UntypedReferenceProvider(_root, referenceRuntime)]);
            var silo = Substitute.For<ILocalSiloDetails>();
            // 仅地址数据：无套接字、端口分配、DNS 或 Silo 启动。
            silo.SiloAddress.Returns(Address);
            silo.GatewayAddress.Returns((SiloAddress)null!);
            Runtime = new InsideRuntimeClient(
                silo, Scopes, messageFactory, loggerFactory, options, trace, referenceActivator,
                new GrainInterfaceTypeResolver(
                    [], _root.GetRequiredService<Orleans.Serialization.TypeSystem.TypeConverter>()),
                new GrainInterfaceTypeToGrainTypeResolver(Substitute.For<IClusterManifestProvider>()),
                deepCopier, Clock, mapping, instruments);

            // 仅构造的传输层对象图。GatewayAddress == null 防止调用会引发异常的工厂。
            // 本地回调、状态和缓存失效路径使用配置好的运行时。
            // 观察者调用主体使用有效的 OneWay 可调用对象，包括失败时。
            var messageCenter = new MessageCenter(
                siloDetails: silo,
                messageFactory: messageFactory,
                catalog: null!,
                gatewayFactory: _ => throw new InvalidOperationException("The hosted drain fixture must not create a gateway."),
                logger: NullLogger<MessageCenter>.Instance,
                siloStatusOracle: Substitute.For<ISiloStatusOracle>(),
                senderManager: null!,
                messagingTrace: new RuntimeMessagingTrace(loggerFactory, messaging, processing),
                messagingInstruments: messaging,
                messagingProcessingInstruments: processing,
                messagingOptions: options,
                placementService: null!,
                grainLocator: null!,
                messageStatisticsSink: new NoOpMessageStatisticsSink());
            var grainFactory = Substitute.For<IInternalGrainFactory>();
            grainFactory.GetGrain(Arg.Any<GrainId>())
                .Returns(call => referenceActivator.CreateReference(call.Arg<GrainId>(), default));
            Hosted = new HostedClient(
                Runtime, silo, NullLogger<HostedClient>.Instance, referenceRuntime, grainFactory,
                messageCenter, trace, deepCopier, referenceActivator, mapping);
            Marker = Hosted.ActivationServices.GetRequiredService<ScopeMarker>();
            ObserverId = Assert.IsType<GrainReference>(Hosted.CreateObjectReference(_observer)).GrainId;

            ILifecycleObserver? observer = null;
            var subscriptions = 0;
            var lifecycle = Substitute.For<ISiloLifecycle>();
            lifecycle.HighestCompletedStage.Returns(0);
            lifecycle.LowestStoppedStage.Returns(int.MaxValue);
            lifecycle.Subscribe(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ILifecycleObserver>())
                .Returns(call =>
                {
                    Assert.Equal("HostedClient", call.Arg<string>());
                    Assert.Equal(ServiceLifecycleStage.RuntimeGrainServices, call.Arg<int>());
                    observer = call.Arg<ILifecycleObserver>();
                    subscriptions++;
                    return _subscription;
                });
            ((ILifecycleParticipant<ISiloLifecycle>)Hosted).Participate(lifecycle);
            Assert.Equal(1, subscriptions);
            _lifecycleObserver = observer ?? throw new InvalidOperationException("HostedClient did not subscribe its lifecycle observer.");
        }

        internal HostedClient Hosted { get; }
        internal InsideRuntimeClient Runtime { get; }
        internal SiloAddress Address { get; } = SiloAddress.New(IPAddress.Loopback, 0, 1);
        internal ScopeTrackingProvider Scopes { get; }
        internal ScopeMarker Marker { get; }
        internal FakeTimeProvider Clock { get; } = new();
        internal ApplicationRequestInstruments ApplicationRequests { get; }
        internal ConcurrentQueue<string> Events { get; } = new();
        internal GrainId Sender { get; } = ClientGrainId.Create("hosted-drain-sender").GrainId;
        internal GrainId ObserverId { get; }
        internal int StopCalls => _stops.Count;
        internal int DisposeReturns => Volatile.Read(ref _disposeReturns);

        internal T Track<T>(T invocation) where T : ScopeCheckingInvokable
        {
            // 在启动任何生命周期或工作线程之前注册所有请求。
            _invocations.Add(invocation);
            return invocation;
        }

        internal Task StartAsync() =>
            WaitAsync(_lifecycleObserver.OnStart(CancellationToken.None), "captured HostedClient OnStart");

        internal Task Stop(CancellationToken cancellationToken)
        {
            // 直接调用：此操作在返回前关闭通道，而不是在安排的工作线程上关闭。
            var stop = _lifecycleObserver.OnStop(cancellationToken);
            _stops.Enqueue(stop);
            return stop;
        }

        internal void Deliver(ScopeCheckingInvokable invocation) => Hosted.ReceiveMessage(new Message
        {
            Id = new CorrelationId(invocation.Id),
            TargetGrain = ObserverId,
            SendingGrain = Sender,
            Direction = Message.Directions.OneWay,
            BodyObject = invocation,
        });

        internal async Task DisposeOnWorkerAsync(string phase)
        {
            var returned = DrainTestHelpers.CreateSignal();
            var worker = Task.Run(() =>
            {
                try
                {
                    ((IDisposable)Hosted).Dispose();
                    Interlocked.Increment(ref _disposeReturns);
                    returned.TrySetResult();
                }
                catch (Exception exception)
                {
                    returned.TrySetException(exception);
                    throw;
                }
            }, TestContext.Current.CancellationToken);
            _workers.Add(worker);
            await WaitAsync(returned.Task, phase);
            await WaitAsync(worker, $"{phase}: worker joined");
        }

        internal Task WaitAsync(Task task, string phase, Func<string>? additionalState = null) =>
            DrainTestHelpers.AwaitPhaseAsync(
                task, phase, () => $"{DescribeState()} {additionalState?.Invoke()}",
                TestContext.Current.CancellationToken);

        internal string DescribeState() =>
            $"{_test}; observer={ObserverId}; " +
            $"requests=[{string.Join("; ", _invocations.Select(invocation => invocation.DescribeState()))}]; " +
            $"stops=[{string.Join(",", _stops.Select(stop => stop.Status))}]; " +
            $"disposeReturns={DisposeReturns}; scopesCreated={Scopes.CreateCount}; " +
            $"wrapperDisposeCalls={Scopes.DisposeCount}; markerDisposals={Marker.DisposeCount}; " +
            $"events=[{string.Join(",", Events)}]";

        internal void AssertScopeLive()
        {
            Assert.Equal(1, Scopes.CreateCount);
            Assert.Equal(0, Scopes.DisposeCount);
            Assert.Equal(0, Marker.DisposeCount);
            Assert.Same(Marker, Hosted.ActivationServices.GetRequiredService<ScopeMarker>());
        }

        internal void AssertExecuted(params ScopeCheckingInvokable[] invocations)
        {
            foreach (var invocation in invocations)
            {
                Assert.Equal(1, invocation.InvocationCount);
                Assert.True(invocation.Exited.Task.IsCompletedSuccessfully, DescribeState());
                Assert.Null(invocation.Failure);
                Assert.Same(_observer, invocation.ObservedTarget);
                Assert.Same(Marker, invocation.ObservedMarker);
                Assert.True(invocation.MarkerLiveOnEntry, DescribeState());
                Assert.True(invocation.MarkerLiveBeforeExit, DescribeState());
            }
        }

        internal void AssertDisposedAfterExecution()
        {
            Assert.Equal(1, Scopes.CreateCount);
            Assert.Equal(1, Scopes.DisposeCount);
            Assert.Equal(1, Marker.DisposeCount);
            Assert.True(Scopes.AllBodiesExitedAtDisposal, DescribeState());
            Assert.True(Scopes.ScopeDisposed.Task.IsCompletedSuccessfully, DescribeState());
        }

        internal async Task CleanupAsync()
        {
            foreach (var invocation in _invocations)
            {
                invocation.ReleaseForCleanup();
            }

            // 清理有独立且不会被取消的兜底机制。如果工作线程或排空未完成，
            // 抛出包含上下文的异常，并刻意为仍在运行的执行实例保留提供程序。
            var drain = Stop(CancellationToken.None);
            foreach (var worker in _workers)
            {
                try
                {
                    await DrainTestHelpers.AwaitCleanupAsync(
                        worker, "cleanup: Dispose worker joined", DescribeState);
                }
                catch (OperationCanceledException) when (worker.IsCanceled && TestContext.Current.CancellationToken.IsCancellationRequested)
                {
                    // Task.Run 可能在工作线程开始之前被取消。此时没有
                    // 工作线程执行需要等待；但独立启动的排空仍必须完成。
                }
            }

            await DrainTestHelpers.AwaitCleanupAsync(
                Task.WhenAll(_stops), "cleanup: actual hosted drains joined", DescribeState);
            await DrainTestHelpers.AwaitCleanupAsync(drain, "cleanup: pump and manager drained", DescribeState);
            // 同时等待实际进入执行的调用主体结束，防止过早排空的回归问题导致
            // 清理在这些已启动的调用主体退出期间释放提供程序。
            // 未调用或被拒绝的消息没有 Exited 信号，此处不等待它们。
            await DrainTestHelpers.AwaitCleanupAsync(
                Task.WhenAll(_invocations.Where(invocation => invocation.InvocationCount != 0)
                    .Select(invocation => invocation.Exited.Task)),
                "cleanup: entered invocation bodies exited", DescribeState);
            ((IDisposable)Hosted).Dispose();
            await DrainTestHelpers.AwaitCleanupAsync(
                Scopes.ScopeDisposed.Task, "cleanup: owned scope disposal completed", DescribeState);
            _subscription.Dispose();

            // ScopeDisposed 在实际作用域的 Dispose 之后发出，而不是在 HostedClient 的
            // 整个后台方法之后发出。剩余的尾部操作仅调用 MessageCenter.SetHostedClient(null)
            // （字段赋值）；它无法使用下方已释放的序列化器或指标根对象。
            // 在此源码快照中 InsideRuntimeClient 没有公开的 Dispose 或停止 API。我们从未
            // 对其调用 Participate、ConsumeServices 或启动它：其监视器没有任务，其定时器属于
            // 这个未推进时间的 FakeTimeProvider，因此无需清理基于实际时间或线程池的定时器。
            _root.Dispose();
            foreach (var invocation in _invocations)
            {
                ((IDisposable)invocation).Dispose();
            }

            GC.KeepAlive(_observer);
        }
    }

    private class ScopeCheckingInvokable : TestInvokable
    {
        private readonly Func<string> _describeState;
        private readonly ConcurrentQueue<string> _events;

        internal ScopeCheckingInvokable(long id, Func<string> describeState, ConcurrentQueue<string> events)
            : base($"hosted-{id}")
        {
            Id = id;
            _describeState = describeState;
            _events = events;
        }

        internal long Id { get; }
        internal TaskCompletionSource ScopeEntered { get; } = DrainTestHelpers.CreateSignal();
        internal object? ObservedTarget { get; private set; }
        internal ScopeMarker? ObservedMarker { get; private set; }
        internal bool MarkerLiveOnEntry { get; private set; }
        internal bool MarkerLiveBeforeExit { get; private set; }
        internal Exception? Failure { get; private set; }

        protected override async ValueTask<Response> InvokeCoreAsync()
        {
            try
            {
                ObservedTarget = TargetHolder?.GetTarget();
                // 通过实际的 LocalObjectData -> HostedClient 组件链解析。
                // 测试还会检查它与 HostedClient.ActivationServices 的对象标识是否一致。
                ObservedMarker = TargetHolder?.GetComponent(typeof(ScopeMarker)) as ScopeMarker
                    ?? throw new InvalidOperationException($"Request {Id} did not receive its hosted scope marker.");
                MarkerLiveOnEntry = ObservedMarker.DisposeCount == 0;
                _events.Enqueue($"{Id}:entered");
                ScopeEntered.TrySetResult();
                await WaitForCompletionAsync();
                MarkerLiveBeforeExit = ObservedMarker.DisposeCount == 0;
                _events.Enqueue($"{Id}:exiting");
                return Response.Completed;
            }
            catch (Exception exception)
            {
                // 生产代码会记录 OneWay 异常，因此保留失败供测试检查，
                // 而不是将基类辅助方法的 finally 或 Exited 信号误认为调用主体执行成功。
                Failure = exception;
                ScopeEntered.TrySetException(exception);
                throw;
            }
        }

        internal string DescribeState() =>
            $"id={Id}, invokes={InvocationCount}, scopeEntered={ScopeEntered.Task.Status}, " +
            $"release={Release.Task.Status}, exited={Exited.Task.Status}, failure={Failure?.Message}";

        protected virtual Task WaitForCompletionAsync() =>
            DrainTestHelpers.AwaitPhaseAsync(
                Release.Task, $"{Id}: body release", _describeState, TestContext.Current.CancellationToken);
    }

    private sealed class CallbackAwaitingScopeInvokable(
        long id, Func<string> describeState, ConcurrentQueue<string> events, CallbackCompletionSource completion)
        : ScopeCheckingInvokable(id, describeState, events)
    {
        internal TaskCompletionSource CallbackAwaiting { get; } = DrainTestHelpers.CreateSignal();
        internal object? CallbackResult { get; private set; }

        protected override async Task WaitForCompletionAsync()
        {
            CallbackAwaiting.TrySetResult();
            await completion.Completed.Task;
            if (completion.Exception is { } exception)
            {
                throw exception;
            }

            CallbackResult = completion.Result;
        }
    }

    private sealed class CancellableScopeInvokable(
        long id, Func<string> describeState, ConcurrentQueue<string> events)
        : ScopeCheckingInvokable(id, describeState, events)
    {
        private readonly CancellationTokenSource _cancellation = new();
        private int _cancelCount;
        internal int CancelCount => Volatile.Read(ref _cancelCount);
        protected override bool SupportsCancellation => true;
        protected override CancellationToken RequestCancellationToken => _cancellation.Token;

        protected override bool CancelRequest()
        {
            Interlocked.Increment(ref _cancelCount);
            _cancellation.Cancel();
            // 允许协作式观察者在取消后正常完成。
            Release.TrySetResult();
            return true;
        }

        protected override void DisposeCore() => _cancellation.Dispose();
    }

    private sealed class ClassifierHeldInvokable : ScopeCheckingInvokable
    {
        private readonly Func<string> _describeState;
        private int _classifierCalls;

        internal ClassifierHeldInvokable(long id, Func<string> describeState, ConcurrentQueue<string> events)
            : base(id, describeState, events) => _describeState = describeState;

        internal TaskCompletionSource ClassifierEntered { get; } = DrainTestHelpers.CreateSignal();
        internal TaskCompletionSource ClassifierRelease { get; } = DrainTestHelpers.CreateSignal();
        internal int ClassifierCalls => Volatile.Read(ref _classifierCalls);

        protected override Type InterfaceType
        {
            get
            {
                // 调用 1 是编排器上的 Hosted.ReceiveMessage。调用 2 是托管
                // 消息泵的 LocalObjectData.ReceiveMessage，紧接在 gate.TryEnter() 之前。
                if (Interlocked.Increment(ref _classifierCalls) == 2)
                {
                    ClassifierEntered.TrySetResult();
                    DrainTestHelpers.AwaitPhaseAsync(
                        ClassifierRelease.Task, $"{Id}: classifier release on pump worker", _describeState,
                        TestContext.Current.CancellationToken)
                        .GetAwaiter().GetResult();
                }

                return typeof(IGrainObserver);
            }
        }

        internal override void ReleaseForCleanup()
        {
            ClassifierRelease.TrySetResult();
            base.ReleaseForCleanup();
        }
    }

    private sealed class ScopeMarker(ConcurrentQueue<string> events) : IDisposable
    {
        private int _disposeCount;
        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            events.Enqueue("marker-disposed");
        }
    }

    private sealed class ScopeTrackingProvider(
        ServiceProvider root,
        Func<bool> allBodiesExited,
        ConcurrentQueue<string> events) : IServiceProvider, IServiceScopeFactory
    {
        private int _createCount;
        private int _disposeCount;
        private int _earlyDisposals;
        internal int CreateCount => Volatile.Read(ref _createCount);
        internal int DisposeCount => Volatile.Read(ref _disposeCount);
        internal bool AllBodiesExitedAtDisposal => DisposeCount != 0 && Volatile.Read(ref _earlyDisposals) == 0;
        internal TaskCompletionSource ScopeDisposed { get; } = DrainTestHelpers.CreateSignal();

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServiceScopeFactory) ? this : root.GetService(serviceType);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _createCount);
            return new CountingScope(root.CreateScope(), this);
        }

        private void DisposeScope(IServiceScope scope)
        {
            // 统计每一次包装器调用，即使实际 DI 会使后续释放成为空操作。
            Interlocked.Increment(ref _disposeCount);
            if (!allBodiesExited())
            {
                Interlocked.Increment(ref _earlyDisposals);
            }

            events.Enqueue("scope-disposing");
            try
            {
                scope.Dispose();
                events.Enqueue("scope-disposed");
                ScopeDisposed.TrySetResult();
            }
            catch (Exception exception)
            {
                ScopeDisposed.TrySetException(exception);
                throw;
            }
        }

        private sealed class CountingScope(IServiceScope scope, ScopeTrackingProvider owner) : IServiceScope
        {
            public IServiceProvider ServiceProvider => scope.ServiceProvider;
            public void Dispose() => owner.DisposeScope(scope);
        }
    }

    private sealed class UntypedReferenceProvider(
        IServiceProvider services,
        IGrainReferenceRuntime runtime) : IGrainReferenceActivatorProvider
    {
        public bool TryGet(
            GrainType grainType,
            GrainInterfaceType interfaceType,
            [NotNullWhen(true)] out IGrainReferenceActivator? activator)
        {
            var shared = new GrainReferenceShared(
                grainType, interfaceType, 0, runtime, InvokeMethodOptions.None,
                services.GetRequiredService<CodecProvider>(),
                services.GetRequiredService<CopyContextPool>(), services);
            activator = new UntypedReferenceActivator(shared);
            return true;
        }

        private sealed class UntypedReferenceActivator(GrainReferenceShared shared) : IGrainReferenceActivator
        {
            public GrainReference CreateReference(GrainId grainId) => GrainReference.FromGrainId(shared, grainId);
        }
    }
}
