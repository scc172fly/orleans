using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NonSilo.Tests.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Orleans.Configuration;
using Orleans.Runtime.MembershipService;
using Orleans.TestingHost.Logging;
using TestExtensions;
using Xunit;
using static Orleans.Runtime.MembershipService.SiloHealthMonitor;

namespace NonSilo.Tests.Membership
{
    /// <summary>
    /// 测试 Silo 健康监控，包括直接及间接探测、故障检测与过时 Silo 处理。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Membership")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class SiloHealthMonitorTests
    {
        private readonly ITestOutputHelper _output;
        private readonly LoggerFactory _loggerFactory;
        private readonly ILocalSiloDetails _localSiloDetails;
        private readonly SiloAddress _localSilo;
        private readonly List<DelegateAsyncTimer> _timers;
        private readonly Channel<(TimeSpan? DelayOverride, TaskCompletionSource<bool> Completion)> _timerCalls;
        private readonly DelegateAsyncTimerFactory _timerFactory;
        private readonly ILocalSiloHealthMonitor _localSiloHealthMonitor;
        private readonly IRemoteSiloProber _prober;
        private readonly MembershipTableManager _membershipService;
        private readonly ClusterMembershipOptions _clusterMembershipOptions;
        private readonly IOptionsMonitor<ClusterMembershipOptions> _optionsMonitor;
        private readonly Channel<ProbeResult> _probeResults;
        private readonly SiloHealthMonitor _monitor;
        private readonly SiloAddress _targetSilo;
        private readonly InMemoryMembershipTable _membershipTable;

        public SiloHealthMonitorTests(ITestOutputHelper output)
        {
            _output = output;
            _loggerFactory = new LoggerFactory(new[] { new XunitLoggerProvider(_output) });

            _localSiloDetails = Substitute.For<ILocalSiloDetails>();
            _localSilo = Silo("127.0.0.1:100@100");
            _localSiloDetails.SiloAddress.Returns(_localSilo);
            _localSiloDetails.DnsHostName.Returns("MyServer11");
            _localSiloDetails.Name.Returns(Guid.NewGuid().ToString("N"));

            _timers = new List<DelegateAsyncTimer>();
            _timerCalls = Channel.CreateUnbounded<(TimeSpan? DelayOverride, TaskCompletionSource<bool> Completion)>();
            _timerFactory = new DelegateAsyncTimerFactory(
                (period, name) =>
                {
                    var t = new DelegateAsyncTimer(
                        overridePeriod =>
                        {
                            var task = new TaskCompletionSource<bool>();
                            _timerCalls.Writer.TryWrite((overridePeriod, task));
                            return task.Task;
                        });
                    _timers.Add(t);
                    return t;
                });

            _localSiloHealthMonitor = Substitute.For<ILocalSiloHealthMonitor>();
            _localSiloHealthMonitor.GetLocalHealthStatus(default, default, default).ReturnsForAnyArgs(new LocalSiloHealthStatus(0, []));

            _prober = Substitute.For<IRemoteSiloProber>();

            _clusterMembershipOptions = new ClusterMembershipOptions();
            _optionsMonitor = Substitute.For<IOptionsMonitor<ClusterMembershipOptions>>();
            _optionsMonitor.CurrentValue.ReturnsForAnyArgs(info => _clusterMembershipOptions);

            var fatalErrorHandler = Substitute.For<IFatalErrorHandler>();
            var membershipGossiper = Substitute.For<IMembershipGossiper>();
            var lifecycle = new SiloLifecycleSubject(_loggerFactory.CreateLogger<SiloLifecycleSubject>());

            _targetSilo = Silo("127.0.0.200:100@100");
            _membershipTable = new(new TableVersion(0, "0"), Entry(_localSilo, SiloStatus.Active), Entry(_targetSilo, SiloStatus.Active));
            _membershipService = new MembershipTableManager(
                localSiloDetails: _localSiloDetails,
                clusterMembershipOptions: Options.Create(_clusterMembershipOptions),
                membershipTable: _membershipTable,
                fatalErrorHandler: fatalErrorHandler,
                gossiper: membershipGossiper,
                log: _loggerFactory.CreateLogger<MembershipTableManager>(),
                timerFactory: new AsyncTimerFactory(_loggerFactory),
                lifecycle,
                timeProvider: TimeProvider.System);

            _probeResults = Channel.CreateBounded<ProbeResult>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.Wait });
            Task onProbeResult(SiloHealthMonitor mon, ProbeResult res)
                => _probeResults.Writer.WriteAsync(res, TestContext.Current.CancellationToken).AsTask();

            _monitor = new SiloHealthMonitor(
                _targetSilo,
                onProbeResult,
                _optionsMonitor,
                _loggerFactory,
                _prober,
                _timerFactory,
                _localSiloHealthMonitor,
                _membershipService,
                _localSiloDetails,
                TimeProvider.System);
        }

        private async Task Shutdown(CancellationToken cancellationToken)
        {
            var stopTask = _monitor.StopAsync(cancellationToken);

            Task.Run(async () =>
            {
                while (!stopTask.IsCompleted && await _timerCalls.Reader.WaitToReadAsync(cancellationToken))
                {
                    while (_timerCalls.Reader.TryRead(out var timerCall))
                    {
                        timerCall.Completion.TrySetResult(false);
                    }
                }
            }, CancellationToken.None).Ignore();

            await stopTask;
            _timerCalls.Writer.TryComplete();
            cancellationToken.ThrowIfCancellationRequested();
        }

        [Fact]
        public async Task DisposalBeforeStartReleasesCancellationAndTimer()
        {
            var source = GetStoppingSource();
            var handle = source.Token.WaitHandle;

            _monitor.Dispose();
            await _monitor.DisposeAsync();
            _monitor.Dispose();

            Assert.True(_monitor.IsCanceled);
            Assert.Equal(1, Assert.Single(_timers).DisposedCounter);
            Assert.Throws<ObjectDisposedException>(() => handle.WaitOne(0));
            Assert.Throws<InvalidOperationException>(() => _monitor.Start());
        }

        [Fact]
        public async Task CanceledStopWaitRetainsSourceUntilWorkerCompletes()
        {
            var source = GetStoppingSource();
            var handle = source.Token.WaitHandle;
            _monitor.Start();
            var tick = await _timerCalls.Reader.ReadAsync(TestContext.Current.CancellationToken);
            using var canceledWait = new CancellationTokenSource();
            canceledWait.Cancel();
            try
            {
                await _monitor.StopAsync(canceledWait.Token);
                var first = _monitor.DisposeAsync().AsTask();
                var second = _monitor.DisposeAsync().AsTask();

                Assert.True(handle.WaitOne(0));
                Assert.False(first.IsCompleted);
                Assert.False(second.IsCompleted);
                Assert.Equal(1, Assert.Single(_timers).DisposedCounter);
                tick.Completion.SetResult(false);
                await Task.WhenAll(first, second).WaitAsync(TestContext.Current.CancellationToken);

                Assert.Throws<ObjectDisposedException>(() => handle.WaitOne(0));
                await _monitor.DisposeAsync();
            }
            finally
            {
                tick.Completion.TrySetResult(false);
                await _monitor.DisposeAsync();
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CancellationFailureStillDrainsWorkerAndReleasesResources(bool callbackThrows)
        {
            var source = GetStoppingSource();
            var token = source.Token;
            var handle = token.WaitHandle;
            _monitor.Start();
            var tick = await _timerCalls.Reader.ReadAsync(TestContext.Current.CancellationToken);
            var expected = new InvalidOperationException("Cancellation callback failed.");
            var callbackThread = 0;
            using var registration = token.Register(() =>
            {
                callbackThread = Environment.CurrentManagedThreadId;
                if (callbackThrows)
                {
                    throw expected;
                }
            });
            try
            {
                var callerThread = Environment.CurrentManagedThreadId;
                var first = _monitor.DisposeAsync().AsTask();
                var second = _monitor.DisposeAsync().AsTask();

                Assert.Equal(callerThread, callbackThread);
                Assert.True(handle.WaitOne(0));
                Assert.False(first.IsCompleted);
                Assert.False(second.IsCompleted);
                Assert.Equal(1, Assert.Single(_timers).DisposedCounter);
                tick.Completion.SetResult(false);
                if (callbackThrows)
                {
                    var exception = await Assert.ThrowsAsync<AggregateException>(() => first);
                    Assert.Same(expected, Assert.Single(exception.InnerExceptions));
                }
                else
                {
                    await first;
                }

                await second;
                Assert.Throws<ObjectDisposedException>(() => handle.WaitOne(0));
                await _monitor.DisposeAsync();
            }
            finally
            {
                tick.Completion.TrySetResult(false);
                await _monitor.DisposeAsync();
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task WorkerCompletionOwnsCancellationRelease(bool timerFails)
        {
            var source = GetStoppingSource();
            var handle = source.Token.WaitHandle;
            _monitor.Start();
            var tick = await _timerCalls.Reader.ReadAsync(TestContext.Current.CancellationToken);
            var running = Assert.IsAssignableFrom<Task>(
                typeof(SiloHealthMonitor).GetField("_runTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_monitor));
            var expected = new InvalidOperationException("Timer failed.");
            try
            {
                if (timerFails)
                {
                    tick.Completion.SetException(expected);
                    var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => running);
                    Assert.Same(expected, exception);
                }
                else
                {
                    tick.Completion.SetResult(false);
                    await running;
                }

                Assert.True(_monitor.IsCanceled);
                Assert.Equal(1, Assert.Single(_timers).DisposedCounter);
                Assert.Throws<ObjectDisposedException>(() => handle.WaitOne(0));
            }
            finally
            {
                tick.Completion.TrySetResult(false);
                await _monitor.DisposeAsync();
            }
        }

        private CancellationTokenSource GetStoppingSource() => Assert.IsType<CancellationTokenSource>(
            typeof(SiloHealthMonitor).GetField("_stoppingCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_monitor));

        [Fact]
        public async Task SiloHealthMonitor_SuccessfulProbe()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            _prober.Probe(default!, default, cancellationToken).ReturnsForAnyArgs(Task.CompletedTask);
            _prober.ProbeIndirectly(default!, default!, default, default, cancellationToken).ThrowsAsyncForAnyArgs(new InvalidOperationException("No"));

            _monitor.Start();

            // 使定时器的当前等待完成。
            var timerCall = await _timerCalls.Reader.ReadAsync(cancellationToken);
            timerCall.Completion.TrySetResult(true);

            // 检查最终的探测结果。
            var probeResult = await _probeResults.Reader.ReadAsync(cancellationToken);
            Assert.Equal(ProbeResultStatus.Succeeded, probeResult.Status);
            Assert.Equal(0, probeResult.FailedProbeCount);
            Assert.True(probeResult.IsDirectProbe);
            Assert.Equal(0, probeResult.IntermediaryHealthDegradationScore);
            await _localSiloHealthMonitor.DidNotReceiveWithAnyArgs().GetStallDurationAsync(default, default, cancellationToken);

            await Shutdown(cancellationToken);
        }

        [Fact]
        public async Task SiloHealthMonitor_SuccessfulDirectProbesAdaptTimeout()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            _prober.Probe(default!, default, cancellationToken).ReturnsForAnyArgs(Task.CompletedTask);
            _monitor.Start();

            for (var i = 0; i < 4; i++)
            {
                var timerCall = await _timerCalls.Reader.ReadAsync(cancellationToken);
                timerCall.Completion.TrySetResult(true);
                var probeResult = await _probeResults.Reader.ReadAsync(cancellationToken);
                Assert.Equal(ProbeResultStatus.Succeeded, probeResult.Status);
                Assert.True(probeResult.IsDirectProbe);
            }

            var testAccessor = (ITestAccessor)_monitor;
            Assert.Equal(4, testAccessor.ProbeTimeoutSampleCount);
            Assert.InRange(
                testAccessor.CurrentProbeTimeout,
                _clusterMembershipOptions.MinProbeTimeout,
                _clusterMembershipOptions.ProbeTimeout);

            await Shutdown(cancellationToken);
        }

        [Fact]
        public async Task SiloHealthMonitor_SuccessfulIndirectProbeDoesNotAdaptTimeout()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            _clusterMembershipOptions.NumMissedProbesLimit = 1;
            var intermediary = Silo("127.0.0.1:1234@1234");
            await _membershipTable.InsertRowAsync(
                Entry(intermediary, SiloStatus.Active, DateTime.UtcNow),
                _membershipTable.Version.Next(),
                cancellationToken);
            await _membershipService.Refresh(cancellationToken: cancellationToken);
            _prober.ProbeIndirectly(default!, default!, default, default, cancellationToken).ReturnsForAnyArgs(new IndirectProbeResponse
            {
                Succeeded = true,
                ProbeResponseTime = TimeSpan.FromMilliseconds(1),
            });

            _monitor.Start();
            var timerCall = await _timerCalls.Reader.ReadAsync(cancellationToken);
            timerCall.Completion.TrySetResult(true);
            var probeResult = await _probeResults.Reader.ReadAsync(cancellationToken);

            Assert.Equal(ProbeResultStatus.Succeeded, probeResult.Status);
            Assert.False(probeResult.IsDirectProbe);
            Assert.Equal(0, ((ITestAccessor)_monitor).ProbeTimeoutSampleCount);

            await Shutdown(cancellationToken);
        }

        [Fact]
        public async Task SiloHealthMonitor_FailedProbe_Timeout()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            _clusterMembershipOptions.ProbeTimeout = TimeSpan.FromSeconds(2);

            _prober.Probe(default!, default, cancellationToken).ReturnsForAnyArgs(
                info => Task.Delay(TimeSpan.FromSeconds(30), info.ArgAt<CancellationToken>(2)));
            _prober.ProbeIndirectly(default!, default!, default, default, cancellationToken).ThrowsAsyncForAnyArgs(new InvalidOperationException("No"));
            _monitor.Start();

            // 使定时器的当前等待完成。
            var timerCall = await _timerCalls.Reader.ReadAsync(cancellationToken);
            timerCall.Completion.TrySetResult(true);

            var probeResult = await _probeResults.Reader.ReadAsync(cancellationToken);
            Assert.Equal(ProbeResultStatus.Failed, probeResult.Status);
            Assert.Equal(1, probeResult.FailedProbeCount);
            Assert.True(probeResult.IsDirectProbe);
            Assert.Equal(0, probeResult.IntermediaryHealthDegradationScore);
            Assert.Equal(0, ((ITestAccessor)_monitor).ProbeTimeoutSampleCount);
            await _localSiloHealthMonitor.Received(1).GetStallDurationAsync(
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>());
            await Shutdown(cancellationToken);
        }

        [Fact]
        public async Task SiloHealthMonitor_TimeoutDuringLocalStall_WaitsForDetectorCycleAndIsInconclusive()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            var options = new ClusterMembershipOptions
            {
                ProbeTimeout = TimeSpan.FromSeconds(5),
                MaxProbeTimeout = TimeSpan.FromSeconds(45),
                EnableIndirectProbes = false,
            };
            var optionsMonitor = Substitute.For<IOptionsMonitor<ClusterMembershipOptions>>();
            optionsMonitor.CurrentValue.Returns(options);
            var prober = Substitute.For<IRemoteSiloProber>();
            var probeEntered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pendingProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            prober.Probe(default!, default, cancellationToken).ReturnsForAnyArgs(call =>
            {
                probeEntered.TrySetResult(call.ArgAt<CancellationToken>(2));
                return pendingProbe.Task;
            });
            var probeResult = new TaskCompletionSource<ProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var detectorQuery = new TaskCompletionSource<(long Start, long End)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var detectorResult = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            var timer = new DilationProbeTimer();
            const long ExpectedStartTimestamp = 123;
            const long ExpectedEndTimestamp = 456;
            var monitor = new SiloHealthMonitor(
                _targetSilo,
                (_, result) =>
                {
                    probeResult.TrySetResult(result);
                    return Task.CompletedTask;
                },
                optionsMonitor,
                _loggerFactory,
                prober,
                new DelegateAsyncTimerFactory((_, _) => timer),
                _localSiloHealthMonitor,
                _membershipService,
                _localSiloDetails,
                timeProvider);
            _localSiloHealthMonitor.GetTimestamp().Returns(ExpectedStartTimestamp, ExpectedEndTimestamp);
            _localSiloHealthMonitor
                .GetStallDurationAsync(default, default, cancellationToken)
                .ReturnsForAnyArgs(call =>
                {
                    detectorQuery.TrySetResult((call.ArgAt<long>(0), call.ArgAt<long>(1)));
                    return new ValueTask<TimeSpan>(detectorResult.Task);
                });

            try
            {
                monitor.Start();
                var firstTick = await timer.Ticks.ReadAsync(cancellationToken);
                firstTick.Completion.SetResult(true);
                var probeCancellationToken = await probeEntered.Task.WaitAsync(cancellationToken);

                timeProvider.Advance(options.ProbeTimeout);

                Assert.True(probeCancellationToken.IsCancellationRequested);
                var interval = await detectorQuery.Task.WaitAsync(cancellationToken);
                Assert.Equal(ExpectedStartTimestamp, interval.Start);
                Assert.Equal(ExpectedEndTimestamp, interval.End);
                Assert.False(probeResult.Task.IsCompleted);

                detectorResult.SetResult(options.ProbeTimeout.Multiply(0.25));
                var result = await probeResult.Task.WaitAsync(cancellationToken);
                Assert.Equal(ProbeResultStatus.Unknown, result.Status);
                Assert.Equal(0, result.FailedProbeCount);
                Assert.Equal(0, ((ITestAccessor)monitor).MissedProbes);

                var nextTick = await timer.Ticks.ReadAsync(cancellationToken);
                nextTick.Completion.SetResult(false);
            }
            finally
            {
                await monitor.StopAsync(cancellationToken);
            }
        }

        [Fact]
        public async Task SiloHealthMonitor_StopDuringStallDetection_DoesNotLogError()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            var options = new ClusterMembershipOptions
            {
                ProbeTimeout = TimeSpan.FromSeconds(5),
                MaxProbeTimeout = TimeSpan.FromSeconds(45),
                EnableIndirectProbes = false,
            };
            var optionsMonitor = Substitute.For<IOptionsMonitor<ClusterMembershipOptions>>();
            optionsMonitor.CurrentValue.Returns(options);
            var prober = Substitute.For<IRemoteSiloProber>();
            var probeEntered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pendingProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            prober.Probe(default!, default, cancellationToken).ReturnsForAnyArgs(call =>
            {
                probeEntered.TrySetResult(call.ArgAt<CancellationToken>(2));
                return pendingProbe.Task;
            });
            var localHealthMonitor = Substitute.For<ILocalSiloHealthMonitor>();
            localHealthMonitor
                .GetLocalHealthStatus(default, default)
                .ReturnsForAnyArgs(new LocalSiloHealthStatus(0, []));
            localHealthMonitor.GetTimestamp().Returns(123, 456);
            var stallQueryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            localHealthMonitor
                .GetStallDurationAsync(default, default, cancellationToken)
                .ReturnsForAnyArgs(call =>
                {
                    stallQueryStarted.TrySetResult();
                    return new ValueTask<TimeSpan>(WaitForCancellation(call.ArgAt<CancellationToken>(2)));
                });
            var timer = new DilationProbeTimer();
            var logProvider = new InMemoryLoggerProvider();
            using var loggerFactory = new LoggerFactory([new XunitLoggerProvider(_output), logProvider]);
            var monitor = new SiloHealthMonitor(
                _targetSilo,
                (_, _) => Task.CompletedTask,
                optionsMonitor,
                loggerFactory,
                prober,
                new DelegateAsyncTimerFactory((_, _) => timer),
                localHealthMonitor,
                _membershipService,
                _localSiloDetails,
                timeProvider);

            try
            {
                monitor.Start();
                var firstTick = await timer.Ticks.ReadAsync(cancellationToken);
                firstTick.Completion.SetResult(true);
                var probeCancellationToken = await probeEntered.Task.WaitAsync(cancellationToken);

                timeProvider.Advance(options.ProbeTimeout);

                Assert.True(probeCancellationToken.IsCancellationRequested);
                await stallQueryStarted.Task.WaitAsync(cancellationToken);
                await monitor.StopAsync(CancellationToken.None);

                Assert.DoesNotContain(logProvider.Buffer.AllEntries, static entry => entry.LogLevel == LogLevel.Error);
            }
            finally
            {
                await monitor.StopAsync(CancellationToken.None);
            }

            static async Task<TimeSpan> WaitForCancellation(CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return TimeSpan.Zero;
            }
        }

        [Fact]
        public async Task SiloHealthMonitor_UnexpectedRunFailure_LogsError()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var prober = Substitute.For<IRemoteSiloProber>();
            prober.Probe(default!, default, cancellationToken).ReturnsForAnyArgs(Task.CompletedTask);
            var timer = new DilationProbeTimer();
            var logProvider = new InMemoryLoggerProvider();
            using var loggerFactory = new LoggerFactory([new XunitLoggerProvider(_output), logProvider]);
            var monitor = new SiloHealthMonitor(
                _targetSilo,
                (_, _) => throw new InvalidOperationException("Unexpected monitor failure"),
                _optionsMonitor,
                loggerFactory,
                prober,
                new DelegateAsyncTimerFactory((_, _) => timer),
                _localSiloHealthMonitor,
                _membershipService,
                _localSiloDetails,
                TimeProvider.System);

            try
            {
                monitor.Start();
                var firstTick = await timer.Ticks.ReadAsync(cancellationToken);
                firstTick.Completion.SetResult(true);
                var secondTick = await timer.Ticks.ReadAsync(cancellationToken);
                secondTick.Completion.SetResult(false);
                await monitor.StopAsync(CancellationToken.None);

                var error = Assert.Single(logProvider.Buffer.GetEntries(LogLevel.Error));
                Assert.IsType<InvalidOperationException>(error.Exception);
                Assert.Equal("Unexpected monitor failure", error.Exception.Message);
            }
            finally
            {
                await monitor.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task SiloHealthMonitor_UnexpectedFailureAfterStop_LogsError()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            var options = new ClusterMembershipOptions
            {
                ProbeTimeout = TimeSpan.FromSeconds(5),
                MaxProbeTimeout = TimeSpan.FromSeconds(45),
                EnableIndirectProbes = false,
            };
            var optionsMonitor = Substitute.For<IOptionsMonitor<ClusterMembershipOptions>>();
            optionsMonitor.CurrentValue.Returns(options);
            var prober = Substitute.For<IRemoteSiloProber>();
            var probeEntered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pendingProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            prober.Probe(default!, default, cancellationToken).ReturnsForAnyArgs(call =>
            {
                probeEntered.TrySetResult(call.ArgAt<CancellationToken>(2));
                return pendingProbe.Task;
            });
            var localHealthMonitor = Substitute.For<ILocalSiloHealthMonitor>();
            localHealthMonitor
                .GetLocalHealthStatus(default, default)
                .ReturnsForAnyArgs(new LocalSiloHealthStatus(0, []));
            localHealthMonitor.GetTimestamp().Returns(123, 456);
            var stallQueryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stallResult = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            localHealthMonitor
                .GetStallDurationAsync(default, default, cancellationToken)
                .ReturnsForAnyArgs(_ =>
                {
                    stallQueryStarted.TrySetResult();
                    return new ValueTask<TimeSpan>(stallResult.Task);
                });
            var timer = new DilationProbeTimer();
            var logProvider = new InMemoryLoggerProvider();
            using var loggerFactory = new LoggerFactory([new XunitLoggerProvider(_output), logProvider]);
            var monitor = new SiloHealthMonitor(
                _targetSilo,
                (_, _) => Task.CompletedTask,
                optionsMonitor,
                loggerFactory,
                prober,
                new DelegateAsyncTimerFactory((_, _) => timer),
                localHealthMonitor,
                _membershipService,
                _localSiloDetails,
                timeProvider);

            try
            {
                monitor.Start();
                var firstTick = await timer.Ticks.ReadAsync(cancellationToken);
                firstTick.Completion.SetResult(true);
                var probeCancellationToken = await probeEntered.Task.WaitAsync(cancellationToken);

                timeProvider.Advance(options.ProbeTimeout);

                Assert.True(probeCancellationToken.IsCancellationRequested);
                await stallQueryStarted.Task.WaitAsync(cancellationToken);
                var stoppingSource = Assert.IsType<CancellationTokenSource>(
                    typeof(SiloHealthMonitor).GetField("_stoppingCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(monitor));
                var handle = stoppingSource.Token.WaitHandle;
                monitor.Dispose();
                var disposal = monitor.DisposeAsync().AsTask();
                Assert.True(handle.WaitOne(0));
                Assert.False(disposal.IsCompleted);
                stallResult.SetException(new InvalidOperationException("Unexpected stall detector failure"));
                await disposal;
                await monitor.StopAsync(CancellationToken.None);
                Assert.Throws<ObjectDisposedException>(() => handle.WaitOne(0));

                var error = Assert.Single(logProvider.Buffer.GetEntries(LogLevel.Error));
                Assert.IsType<InvalidOperationException>(error.Exception);
                Assert.Equal("Unexpected stall detector failure", error.Exception.Message);
            }
            finally
            {
                stallResult.TrySetResult(TimeSpan.Zero);
                await monitor.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task SiloHealthMonitor_FailedProbe_Exception()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            _clusterMembershipOptions.ProbeTimeout = TimeSpan.FromSeconds(2);

            _prober.Probe(default!, default, cancellationToken).ThrowsAsyncForAnyArgs(new Exception("nope"));
            _prober.ProbeIndirectly(default!, default!, default, default, cancellationToken).ThrowsAsyncForAnyArgs(new InvalidOperationException("No"));
            _monitor.Start();

            // 使定时器的当前等待完成。
            var timerCall = await _timerCalls.Reader.ReadAsync(cancellationToken);
            timerCall.Completion.TrySetResult(true);

            // 直接抛出异常，而非使探测超时。
            _prober.WhenForAnyArgs(s => s.Probe(default!, default, cancellationToken)).Throw(new Exception("nope"));
            timerCall = await _timerCalls.Reader.ReadAsync(cancellationToken);
            timerCall.Completion.TrySetResult(true);

            var probeResult = await _probeResults.Reader.ReadAsync(cancellationToken);
            Assert.Equal(ProbeResultStatus.Failed, probeResult.Status);
            Assert.Equal(1, probeResult.FailedProbeCount);
            Assert.True(probeResult.IsDirectProbe);
            Assert.Equal(0, probeResult.IntermediaryHealthDegradationScore);
            Assert.Equal(0, ((ITestAccessor)_monitor).ProbeTimeoutSampleCount);
            await _localSiloHealthMonitor.DidNotReceiveWithAnyArgs().GetStallDurationAsync(default, default, cancellationToken);

            await Shutdown(cancellationToken);
        }

        [Fact]
        public async Task SiloHealthMonitor_Indirect_FailedProbe()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            _clusterMembershipOptions.ProbeTimeout = TimeSpan.FromSeconds(2);
            _clusterMembershipOptions.EnableIndirectProbes = true;

            _prober.Probe(default!, default, cancellationToken).ThrowsAsyncForAnyArgs(info => new Exception("nonono!"));
            _prober.ProbeIndirectly(default!, default!, default, default, cancellationToken).ReturnsForAnyArgs(new IndirectProbeResponse
            {
                FailureMessage = "fail",
                IntermediaryHealthScore = 0,
                ProbeResponseTime = TimeSpan.FromSeconds(1),
                Succeeded = false
            });
            _monitor.Start();

            // 使定时器的当前等待完成。
            var timerCall = await _timerCalls.Reader.ReadAsync(cancellationToken);
            timerCall.Completion.TrySetResult(true);

            var probeResult = await _probeResults.Reader.ReadAsync(cancellationToken);
            Assert.Equal(ProbeResultStatus.Failed, probeResult.Status);
            Assert.Equal(1, probeResult.FailedProbeCount);
            Assert.True(probeResult.IsDirectProbe);
            Assert.Equal(0, probeResult.IntermediaryHealthDegradationScore);

            timerCall = await _timerCalls.Reader.ReadAsync(cancellationToken);
            timerCall.Completion.TrySetResult(true);

            var otherSilo = Silo("127.0.0.1:1234@1234");
            await _membershipTable.InsertRowAsync(Entry(_monitor.TargetSiloAddress, SiloStatus.Active), _membershipTable.Version.Next(), cancellationToken);
            await _membershipTable.InsertRowAsync(Entry(otherSilo, SiloStatus.Joining), _membershipTable.Version.Next(), cancellationToken);
            await _membershipService.Refresh(cancellationToken: cancellationToken);

            // 只有一个其他活跃 Silo（目标 Silo），因此无法执行间接探测。
            probeResult = await _probeResults.Reader.ReadAsync(cancellationToken);
            Assert.Equal(ProbeResultStatus.Failed, probeResult.Status);
            Assert.Equal(2, probeResult.FailedProbeCount);
            Assert.True(probeResult.IsDirectProbe);
            Assert.Equal(0, probeResult.IntermediaryHealthDegradationScore);

            // 使另一个 Silo 进入活跃状态，以便有中间 Silo 可用于间接探测。
            var etag = (await _membershipTable.ReadAllAsync(cancellationToken)).Members.Where(kv => kv.Item1.SiloAddress.Equals(otherSilo)).Single().Item2;
            await _membershipTable.UpdateRowAsync(Entry(otherSilo, SiloStatus.Active, iAmAliveTime: DateTime.UtcNow), etag, _membershipTable.Version.Next(), cancellationToken);
            await _membershipService.Refresh(cancellationToken: cancellationToken);

            _prober.ClearReceivedCalls();
            timerCall = await _timerCalls.Reader.ReadAsync(cancellationToken);
            timerCall.Completion.TrySetResult(true);

            // 由于存在另一个活跃 Silo，将执行间接探测。
            probeResult = await _probeResults.Reader.ReadAsync(cancellationToken);
            Assert.Equal(ProbeResultStatus.Failed, probeResult.Status);
            Assert.Equal(3, probeResult.FailedProbeCount);
            Assert.False(probeResult.IsDirectProbe);
            Assert.Equal(0, probeResult.IntermediaryHealthDegradationScore);

            // 确保选择了正确的中间 Silo。
            var probeCall = _prober.ReceivedCalls().Single();
            var args = probeCall.GetArguments();
            var intermediary = Assert.IsType<SiloAddress>(args[0]);
            Assert.Equal(otherSilo, intermediary);
            Assert.Equal(TimeSpan.FromSeconds(2), Assert.IsType<TimeSpan>(args[2]));

            // 确保不考虑来自不健康中间 Silo 的否定结果。
            _prober.ProbeIndirectly(default!, default!, default, default, cancellationToken).ReturnsForAnyArgs(new IndirectProbeResponse
            {
                FailureMessage = "fail",
                IntermediaryHealthScore = 1,
                ProbeResponseTime = TimeSpan.FromSeconds(1),
                Succeeded = false
            });

            _prober.ClearReceivedCalls();
            timerCall = await _timerCalls.Reader.ReadAsync(cancellationToken);
            timerCall.Completion.TrySetResult(true);

            // 失败探测次数不应增加，状态应为 "unknown"，健康评分应为 1。
            probeResult = await _probeResults.Reader.ReadAsync(cancellationToken);
            Assert.Equal(ProbeResultStatus.Unknown, probeResult.Status);
            Assert.Equal(3, probeResult.FailedProbeCount);
            Assert.False(probeResult.IsDirectProbe);
            Assert.Equal(1, probeResult.IntermediaryHealthDegradationScore);

            // 确保选择了正确的中间 Silo。
            probeCall = _prober.ReceivedCalls().Single();
            args = probeCall.GetArguments();
            intermediary = Assert.IsType<SiloAddress>(args[0]);
            Assert.Equal(otherSilo, intermediary);

            // 发现所选中间 Silo 不健康后，后续探测应
            // 直接执行（因为没有其他 Silo 可用作中间 Silo）。
            _prober.ClearReceivedCalls();
            timerCall = await _timerCalls.Reader.ReadAsync(cancellationToken);
            timerCall.Completion.TrySetResult(true);

            probeResult = await _probeResults.Reader.ReadAsync(cancellationToken);
            Assert.Equal(ProbeResultStatus.Failed, probeResult.Status);
            Assert.Equal(4, probeResult.FailedProbeCount);
            Assert.True(probeResult.IsDirectProbe);
            Assert.Equal(0, probeResult.IntermediaryHealthDegradationScore);

            // 确保实际被直接探测的是目标 Silo。
            probeCall = _prober.ReceivedCalls().Single();
            args = probeCall.GetArguments();
            var target = Assert.IsType<SiloAddress>(args[0]);
            Assert.Equal(_monitor.TargetSiloAddress, target);

            await Shutdown(cancellationToken);
        }

        [Fact]
        public async Task SiloHealthMonitor_IndirectProbe_SkipsStaleSilo()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            _clusterMembershipOptions.EnableIndirectProbes = true;
            _clusterMembershipOptions.ProbeTimeout = TimeSpan.FromSeconds(2);

            // 使直接探测失败。
            _prober.Probe(default!, default, cancellationToken).ThrowsAsyncForAnyArgs(new Exception("Direct probe failing."));

            // 启动监控器并触发一个计时器周期，尝试直接探测（该探测失败）。
            _monitor.Start();
            var timerCall = await _timerCalls.Reader.ReadAsync(cancellationToken);
            timerCall.Completion.TrySetResult(true);
            var firstProbeResult = await _probeResults.Reader.ReadAsync(cancellationToken);
            Assert.True(firstProbeResult.IsDirectProbe);
            Assert.Equal(ProbeResultStatus.Failed, firstProbeResult.Status);

            // 现在添加一个“过时”Silo 和一个“新鲜”Silo（均为 Active 状态）。
            // 此操作在第一次直接探测失败后发生，与上方测试采用的方法相同。
            var staleSilo = Silo("127.0.0.1:3333@3333");
            await _membershipTable.InsertRowAsync(
                Entry(staleSilo, SiloStatus.Active, DateTime.UtcNow - TimeSpan.FromMinutes(30)),
                _membershipTable.Version.Next(),
                cancellationToken);
            _prober.ClearReceivedCalls();

            // 触发另一个计时器周期，此时将尝试间接探测。
            timerCall = await _timerCalls.Reader.ReadAsync(cancellationToken);
            timerCall.Completion.TrySetResult(true);
            var probeResult = await _probeResults.Reader.ReadAsync(cancellationToken);

            // 验证由于跳过过时的中间 Silo，此次执行的是直接探测。
            Assert.True(probeResult.IsDirectProbe);
            var call = _prober.ReceivedCalls().LastOrDefault();
            var args = call?.GetArguments();
            var probedSilo = Assert.IsType<SiloAddress>(args?[0]);
            Assert.Equal(_targetSilo, probedSilo);
            Assert.Equal(_targetSilo, _monitor.TargetSiloAddress);

            await Shutdown(cancellationToken);
        }

        [Theory]
        [InlineData(true, 2, 3)]
        [InlineData(false, 8, 1)]
        public async Task SiloHealthMonitor_DirectProbeTimeoutUsesConfiguredLocalHealthDilation(
            bool extendProbeTimeout,
            int localHealthScore,
            int expectedTimeoutFactor)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            var options = new ClusterMembershipOptions
            {
                ProbeTimeout = TimeSpan.FromSeconds(5),
                MaxProbeTimeout = TimeSpan.FromSeconds(45),
                ExtendProbeTimeoutDuringDegradation = extendProbeTimeout,
                EnableIndirectProbes = false,
            };
            var optionsMonitor = Substitute.For<IOptionsMonitor<ClusterMembershipOptions>>();
            optionsMonitor.CurrentValue.Returns(options);
            var localHealthMonitor = Substitute.For<ILocalSiloHealthMonitor>();
            localHealthMonitor
                .GetLocalHealthStatus(default, default)
                .ReturnsForAnyArgs(new LocalSiloHealthStatus(localHealthScore, []));
            var prober = Substitute.For<IRemoteSiloProber>();
            var probeEntered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pendingProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            prober.Probe(default!, default, cancellationToken).ReturnsForAnyArgs(call =>
            {
                probeEntered.TrySetResult(call.ArgAt<CancellationToken>(2));
                return pendingProbe.Task;
            });
            var probeResult = new TaskCompletionSource<ProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var timer = new DilationProbeTimer();
            var timerFactory = new DelegateAsyncTimerFactory((_, _) => timer);
            var monitor = new SiloHealthMonitor(
                _targetSilo,
                (_, result) =>
                {
                    probeResult.TrySetResult(result);
                    return Task.CompletedTask;
                },
                optionsMonitor,
                _loggerFactory,
                prober,
                timerFactory,
                localHealthMonitor,
                _membershipService,
                _localSiloDetails,
                timeProvider);

            try
            {
                monitor.Start();
                var firstTick = await timer.Ticks.ReadAsync(cancellationToken);
                firstTick.Completion.SetResult(true);
                var probeCancellationToken = await probeEntered.Task.WaitAsync(cancellationToken);

                if (extendProbeTimeout)
                {
                    localHealthMonitor.Received(1).GetLocalHealthStatus(
                        options.ProbeTimeout,
                        LocalSiloHealthCheckCategory.Local);
                }
                else
                {
                    localHealthMonitor.DidNotReceiveWithAnyArgs().GetLocalHealthStatus(default, default);
                }

                await prober.Received(1).Probe(_targetSilo, 1, probeCancellationToken);

                var dilatedTimeout = options.ProbeTimeout.Multiply(expectedTimeoutFactor);
                timeProvider.Advance(dilatedTimeout - TimeSpan.FromTicks(1));
                Assert.False(probeCancellationToken.IsCancellationRequested);
                Assert.False(probeResult.Task.IsCompleted);

                timeProvider.Advance(TimeSpan.FromTicks(1));
                Assert.True(probeCancellationToken.IsCancellationRequested);

                var result = await probeResult.Task.WaitAsync(cancellationToken);
                Assert.Equal(ProbeResultStatus.Failed, result.Status);
                Assert.Equal(1, result.FailedProbeCount);
                Assert.True(result.IsDirectProbe);
                Assert.Equal(0, result.IntermediaryHealthDegradationScore);

                var nextTick = await timer.Ticks.ReadAsync(cancellationToken);
                Assert.Equal(TimeSpan.Zero, nextTick.DelayOverride);
                nextTick.Completion.SetResult(false);
            }
            finally
            {
                await monitor.StopAsync(cancellationToken);
            }
        }

        [Theory]
        [InlineData(5, 1, 4)]
        [InlineData(5, 5, 0)]
        [InlineData(5, 6, 0)]
        public void CalculateNextProbeDelay_MeasuresFromPreviousProbeStart(
            int probePeriodSeconds,
            int elapsedSeconds,
            int expectedDelaySeconds)
        {
            var delay = CalculateNextProbeDelay(
                TimeSpan.FromSeconds(probePeriodSeconds),
                TimeSpan.FromSeconds(elapsedSeconds));

            Assert.Equal(TimeSpan.FromSeconds(expectedDelaySeconds), delay);
        }

        private sealed class DilationProbeTimer : IAsyncTimer
        {
            private readonly object _lock = new();
            private readonly Channel<ProbeTimerTick> _ticks = Channel.CreateUnbounded<ProbeTimerTick>();
            private bool _disposed;

            public ChannelReader<ProbeTimerTick> Ticks => _ticks.Reader;

            public Task<bool> NextTick(TimeSpan? overrideDelay = null)
            {
                lock (_lock)
                {
                    if (_disposed)
                    {
                        return Task.FromResult(false);
                    }

                    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    if (!_ticks.Writer.TryWrite(new(overrideDelay, completion)))
                    {
                        throw new InvalidOperationException("Unable to publish the next timer tick.");
                    }

                    return completion.Task;
                }
            }

            public bool CheckHealth(DateTime lastCheckTime, out string reason)
            {
                reason = string.Empty;
                return true;
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                    _ticks.Writer.TryComplete();
                    while (_ticks.Reader.TryRead(out var tick))
                    {
                        tick.Completion.TrySetResult(false);
                    }
                }
            }
        }

        private readonly record struct ProbeTimerTick(TimeSpan? DelayOverride, TaskCompletionSource<bool> Completion);

        private static SiloAddress Silo(string value) => SiloAddress.FromParsableString(value);

        private static MembershipEntry Entry(SiloAddress address, SiloStatus status, DateTimeOffset iAmAliveTime = default) => new()
        {
            SiloAddress = address,
            Status = status,
            StartTime = iAmAliveTime.UtcDateTime,
            IAmAliveTime = iAmAliveTime.UtcDateTime
        };
    }
}
