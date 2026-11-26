using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Orleans.Durable.Jobs;
using Orleans.Runtime.Messaging;
using Xunit;

namespace NonSilo.Tests.ScheduledJobs;

[TestCategory("BVT"), TestCategory("DurableJobs")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableJobs")]
public class ShardExecutorTests
{
    [Fact]
    public async Task RunShardAsync_WhenNotOverloaded_ProcessesJobsWithoutDelay()
    {
        var options = CreateOptions(maxConcurrentJobs: 10);
        var overloadDetector = CreateOverloadDetector(isOverloaded: false);
        var jobs = CreateJobs(3);
        var shard = CreateJobShard(jobs);
        var grainFactory = CreateGrainFactory();
        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance);

        var completedJobs = new List<string>();
        ConfigureGrainFactoryToTrackCompletions(grainFactory, completedJobs);

        await executor.RunShardAsync(shard, CancellationToken.None);

        // 对所有工作都进行了核查,并删除了碎片
        Assert.Equal(3, completedJobs.Count);
        Assert.Contains("job-0", completedJobs);
        Assert.Contains("job-1", completedJobs);
        Assert.Contains("job-2", completedJobs);

        await shard.Received(3).RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunShardAsync_WhenOverloaded_PausesAndRetriesWithBackoffDelay()
    {
        var options = CreateOptions(maxConcurrentJobs: 10, overloadBackoffDelay: TimeSpan.FromMilliseconds(50));
        var overloadDetector = Substitute.For<IOverloadDetector>();
        var jobs = CreateJobs(2);
        var shard = CreateJobShard(jobs);
        var grainFactory = CreateGrainFactory();
        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance);

        var completedJobs = new List<string>();
        ConfigureGrainFactoryToTrackCompletions(grainFactory, completedJobs);

        // 模拟系统初始超载过负荷, 3次检查后清理
        var checkCount = 0;
        overloadDetector.IsOverloaded.Returns(_ =>
        {
            checkCount++;
            return checkCount <= 3;
        });

        await executor.RunShardAsync(shard, CancellationToken.None);

        // 超载清除后, 工作应该成功完成, 探测器应该多次检查
        Assert.Equal(2, completedJobs.Count);
        Assert.True(checkCount > 3, $"Expected multiple overload checks, got {checkCount}");
    }

    [Fact]
    public async Task RunShardAsync_WhenOverloadTransitionsDuringProcessing_HandlesStateChanges()
    {
        var options = CreateOptions(maxConcurrentJobs: 10, overloadBackoffDelay: TimeSpan.FromMilliseconds(10));
        var overloadDetector = Substitute.For<IOverloadDetector>();
        var grainFactory = CreateGrainFactory();
        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance);

        var completedJobs = new List<string>();
        ConfigureGrainFactoryToTrackCompletions(grainFactory, completedJobs);

        // 工作逐渐到达, 以便让超负荷状态在处理过程中切换
        var shard = CreateJobShardWithDelayedYield(5, TimeSpan.FromMilliseconds(10));

        // 代理超载状态 与每张支票: 超载, 不超载,超载...
        var checkCount = 0;
        overloadDetector.IsOverloaded.Returns(_ =>
        {
            checkCount++;
            return checkCount % 2 == 1;
        });

        await executor.RunShardAsync(shard, CancellationToken.None);

        // 所有的岗位应该都完成 尽管超负荷状态很复杂
        Assert.Equal(5, completedJobs.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Contains($"job-{i}", completedJobs);
        }
    }

    [Fact]
    public async Task RunShardAsync_RespectsMaxConcurrentJobsPerSilo_WhileCheckingOverload()
    {
        var maxConcurrent = 3;
        var options = CreateOptions(maxConcurrentJobs: maxConcurrent);
        var overloadDetector = CreateOverloadDetector(isOverloaded: false);
        var grainFactory = CreateGrainFactory();
        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance);

        var currentConcurrent = 0;
        var maxObservedConcurrent = 0;
        var concurrentLock = new object();

        var jobs = CreateJobs(10);
        var shard = CreateJobShard(jobs);

        // 跟踪最大同时执行任务数
        ConfigureGrainFactoryWithSlowJobExecution(grainFactory, async () =>
        {
            lock (concurrentLock)
            {
                currentConcurrent++;
                if (currentConcurrent > maxObservedConcurrent)
                {
                    maxObservedConcurrent = currentConcurrent;
                }
            }

            await Task.Delay(50);

            lock (concurrentLock)
            {
                currentConcurrent--;
            }
        });

        await executor.RunShardAsync(shard, CancellationToken.None);

        // 核查同货币值的限制得到遵守,同时执行的工作
        Assert.True(maxObservedConcurrent <= maxConcurrent,
            $"Max concurrent jobs was {maxObservedConcurrent}, but limit was {maxConcurrent}");
        Assert.True(maxObservedConcurrent > 1,
            "Expected some concurrent execution");
    }

    [Fact]
    public async Task RunShardAsync_WhenCancelledDuringOverloadBackoff_CancelsCleanly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = CreateOptions(maxConcurrentJobs: 10, overloadBackoffDelay: TimeSpan.FromSeconds(10));
        var overloadDetector = CreateOverloadDetector(isOverloaded: true);
        var jobs = CreateJobs(5);
        var shard = CreateJobShard(jobs);
        var grainFactory = CreateGrainFactory();
        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance);

        var completedJobs = new List<string>();
        ConfigureGrainFactoryToTrackCompletions(grainFactory, completedJobs);

        // 启动后不久取消取消, 而执行程序正在等待超载清除
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await executor.RunShardAsync(shard, cts.Token);
        }).WaitAsync(cancellationToken);

        // 不应该执行任何任务, 因为后端等待时发生取消事件
        Assert.Empty(completedJobs);
    }

    [Fact]
    public async Task RunShardAsync_WhenJobFailsDuringOverload_ContinuesOverloadChecking()
    {
        var options = CreateOptions(
            maxConcurrentJobs: 10,
            overloadBackoffDelay: TimeSpan.FromMilliseconds(10),
            shouldRetry: (context, ex) => DateTimeOffset.UtcNow.AddSeconds(1)
        );
        var overloadDetector = Substitute.For<IOverloadDetector>();
        var grainFactory = CreateGrainFactory();
        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance);

        var jobs = CreateJobs(3);
        var shard = CreateJobShard(jobs);

        var completedJobs = new List<string>();
        var failedJobs = new List<string>();
        var jobExecutionCount = 0;

        // 定期报告超负荷,以测试与工作失败的互动
        var checkCount = 0;
        overloadDetector.IsOverloaded.Returns(_ =>
        {
            checkCount++;
            return checkCount % 3 == 1;
        });

        // 第一份工作失败,剩余工作成功
        ConfigureGrainFactoryWithSelectiveFailures(grainFactory, completedJobs, failedJobs, ref jobExecutionCount);

        await executor.RunShardAsync(shard, CancellationToken.None);

        // 失败的工作应安排重新试入,成功的工作应删除
        Assert.Equal(2, completedJobs.Count);
        Assert.Single(failedJobs);

        await shard.Received(1).RetryJobLaterAsync(
            Arg.Any<IJobRunContext>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());

        await shard.Received(2).RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunShardAsync_WaitsForShardStartTime_BeforeProcessing()
    {
        var timeProvider = new TimerTrackingFakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var startDelay = TimeSpan.FromMinutes(1);
        var options = CreateOptions(maxConcurrentJobs: 10);
        var overloadDetector = CreateOverloadDetector(isOverloaded: false);
        var jobs = CreateJobs(1, timeProvider.GetUtcNow().AddSeconds(-1));
        var shard = CreateJobShard(jobs, startTime: timeProvider.GetUtcNow().Add(startDelay));
        var grainFactory = CreateGrainFactory();
        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance, timeProvider);

        var completedJobs = new List<string>();
        ConfigureGrainFactoryToTrackCompletions(grainFactory, completedJobs);
        var timerCreated = timeProvider.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var runTask = executor.RunShardAsync(shard, CancellationToken.None);

        await timerCreated;
        Assert.False(runTask.IsCompleted);
        Assert.Empty(completedJobs);

        timeProvider.Advance(startDelay - TimeSpan.FromTicks(1));
        Assert.False(runTask.IsCompleted);
        Assert.Empty(completedJobs);

        timeProvider.Advance(TimeSpan.FromTicks(1));

        await runTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Single(completedJobs);
    }

    [Fact]
    public async Task RunShardAsync_WaitsForAllJobsToComplete_BeforeReturning()
    {
        var options = CreateOptions(maxConcurrentJobs: 5);
        var overloadDetector = CreateOverloadDetector(isOverloaded: false);
        var jobs = CreateJobs(5);
        var shard = CreateJobShard(jobs);
        var grainFactory = CreateGrainFactory();
        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance);

        var completedJobs = new List<string>();
        var runningJobs = 0;
        var lockObj = new object();

        // 模拟慢速执行任务,以确保同时运行
        ConfigureGrainFactoryWithSlowJobExecution(grainFactory, async () =>
        {
            lock (lockObj) { runningJobs++; }
            await Task.Delay(100);
            lock (lockObj)
            {
                runningJobs--;
                completedJobs.Add($"job-{completedJobs.Count}");
            }
        });

        await executor.RunShardAsync(shard, CancellationToken.None);

        // 核查返回 RunShardAsync 之前完成的所有工作
        Assert.Equal(0, runningJobs);
        Assert.Equal(5, completedJobs.Count);
    }

    [Fact]
    public async Task RunShardAsync_WhenJobReturnsInProgress_EntersPollingLoopUntilCompletion()
    {
        var options = CreateOptions(maxConcurrentJobs: 10);
        var overloadDetector = CreateOverloadDetector(isOverloaded: false);
        var jobs = CreateJobs(1);
        var shard = CreateJobShard(jobs);

        var (grainFactory, callBox) = CreateGrainFactoryWithPollingBehavior();

        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance);

        await executor.RunShardAsync(shard, CancellationToken.None);

        // 核查 HandleDurableJobAsync 调用4次(1次初次加3投票)
        Assert.Equal(4, callBox.Value);

        // 完成后,核查工作被删除
        await shard.Received(1).RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunShardAsync_WhenJobReturnsInProgress_UsesTimeProvider()
    {
        var timeProvider = new TimerTrackingFakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var options = CreateOptions(maxConcurrentJobs: 10);
        var overloadDetector = CreateOverloadDetector(isOverloaded: false);
        var jobs = CreateJobs(1, timeProvider.GetUtcNow().AddSeconds(-1));
        var shard = CreateJobShard(jobs, startTime: timeProvider.GetUtcNow().AddMinutes(-1));
        var firstCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var grainFactory = Substitute.For<IInternalGrainFactory>();
        var extension = Substitute.For<IDurableJobReceiverExtension>();
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var currentCall = Interlocked.Increment(ref callCount);
                if (currentCall == 1)
                {
                    firstCall.SetResult();
                    return DurableJobRunResult.InProgress(TimeSpan.FromSeconds(5));
                }

                secondCall.SetResult();
                return DurableJobRunResult.Completed;
            });
        grainFactory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);
        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance, timeProvider);

        var runTask = executor.RunShardAsync(shard, CancellationToken.None);

        await firstCall.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await timeProvider.TimerCreated.WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        Assert.False(secondCall.Task.IsCompleted);

        timeProvider.Advance(TimeSpan.FromSeconds(5));

        await secondCall.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await runTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await shard.Received(1).RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunShardAsync_WhenOverloaded_UsesTimeProviderForBackoff()
    {
        var timeProvider = new TimerTrackingFakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var options = CreateOptions(maxConcurrentJobs: 10, overloadBackoffDelay: TimeSpan.FromSeconds(5));
        var overloadDetector = Substitute.For<IOverloadDetector>();
        var overloaded = true;
        overloadDetector.IsOverloaded.Returns(_ => Volatile.Read(ref overloaded));
        var jobs = CreateJobs(1, timeProvider.GetUtcNow().AddSeconds(-1));
        var shard = CreateJobShard(jobs, startTime: timeProvider.GetUtcNow().AddMinutes(-1));
        var grainFactory = CreateGrainFactory();
        var jobHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfigureGrainFactoryWithSlowJobExecution(grainFactory, () =>
        {
            jobHandled.SetResult();
            return Task.CompletedTask;
        });
        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance, timeProvider);

        var runTask = executor.RunShardAsync(shard, CancellationToken.None);

        await timeProvider.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(jobHandled.Task.IsCompleted);

        Volatile.Write(ref overloaded, false);
        timeProvider.Advance(options.Value.OverloadBackoffDelay);

        await jobHandled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await runTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunShardAsync_WhenJobReturnsInProgressThenFails_HandlesFailureCorrectly()
    {
        var options = CreateOptions(
            maxConcurrentJobs: 10,
            shouldRetry: (context, ex) => DateTimeOffset.UtcNow.AddSeconds(1)
        );
        var overloadDetector = CreateOverloadDetector(isOverloaded: false);
        var jobs = CreateJobs(1);
        var shard = CreateJobShard(jobs);

        var (grainFactory, callBox) = CreateGrainFactoryWithPollingThenFailure();

        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance);

        await executor.RunShardAsync(shard, CancellationToken.None);

        // 核查 HandleDurableJobAsync 调用4次(1个初始+2在建结果+1失败)
        Assert.Equal(4, callBox.Value);

        // 校验工作已排入重试( 未删除)
        await shard.Received(1).RetryJobLaterAsync(
            Arg.Any<IJobRunContext>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());

        await shard.DidNotReceive().RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunShardAsync_WhenRetryPersistenceFails_PropagatesAfterReleasingConcurrencyAndContinuingProcessing()
    {
        var options = CreateOptions(
            maxConcurrentJobs: 1,
            shouldRetry: (context, ex) => DateTimeOffset.UtcNow.AddSeconds(1)
        );
        var overloadDetector = CreateOverloadDetector(isOverloaded: false);
        var jobs = CreateJobs(2);
        var firstJobRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shard = CreateJobShard(jobs, firstJobRegistered: firstJobRegistered);
        var grainFactory = CreateGrainFactory();
        var completedJobs = new List<string>();
        var failedJobs = new List<string>();
        var jobExecutionCount = 0;
        ConfigureGrainFactoryWithSelectiveFailures(grainFactory, completedJobs, failedJobs, ref jobExecutionCount);
        var retryPersistenceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failRetryPersistence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expectedException = new InvalidOperationException("Simulated retry persistence failure");

        shard.RetryJobLaterAsync(
            Arg.Any<IJobRunContext>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                retryPersistenceStarted.TrySetResult();
                await failRetryPersistence.Task;
                return DurableJobMutationResult.Applied;
            });

        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var runTask = executor.RunShardAsync(shard, cts.Token);

        await Task.WhenAll(firstJobRegistered.Task, retryPersistenceStarted.Task).WaitAsync(cts.Token);
        Assert.True(failRetryPersistence.TrySetException(expectedException));

        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => runTask);

        Assert.Same(expectedException, actualException);
        Assert.Single(failedJobs);
        Assert.Single(completedJobs);
        await shard.Received(1).RetryJobLaterAsync(Arg.Any<IJobRunContext>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await shard.Received(1).RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunShardAsync_WhenHandlerCancelsWithoutAttemptCancellation_UsesFailureRetryPolicy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var retryPolicyInvoked = false;
        var options = CreateOptions(
            maxConcurrentJobs: 10,
            shouldRetry: (_, exception) =>
            {
                retryPolicyInvoked = true;
                Assert.IsType<TaskCanceledException>(exception);
                return DateTimeOffset.UtcNow.AddSeconds(1);
            }
        );
        var overloadDetector = CreateOverloadDetector(isOverloaded: false);
        var jobs = CreateJobs(1);
        var shard = CreateJobShard(jobs);
        var grainFactory = CreateGrainFactoryWithCanceledExecution();
        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance);

        await executor.RunShardAsync(shard, cancellationToken);

        Assert.True(retryPolicyInvoked);
        await shard.Received(1).RetryJobLaterAsync(
            Arg.Any<IJobRunContext>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await shard.DidNotReceive().RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunShardAsync_WhenAttemptCancellationIsRequested_DoesNotRetryOrRemove()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var retryPolicyInvoked = false;
        var options = CreateOptions(
            maxConcurrentJobs: 10,
            shouldRetry: (_, _) =>
            {
                retryPolicyInvoked = true;
                return DateTimeOffset.UtcNow.AddSeconds(1);
            });
        var overloadDetector = CreateOverloadDetector(isOverloaded: false);
        var jobs = CreateJobs(1);
        var shard = CreateJobShard(jobs);
        var grainFactory = Substitute.For<IInternalGrainFactory>();
        var extension = Substitute.For<IDurableJobReceiverExtension>();
        var attemptStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(call => ExecuteAsync(call.ArgAt<CancellationToken>(1)));
        grainFactory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);
        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance);
        using var attemptCancellation = new CancellationTokenSource();

        var runTask = executor.RunShardAsync(shard, attemptCancellation.Token);
        await attemptStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        attemptCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask.WaitAsync(cancellationToken));
        Assert.False(retryPolicyInvoked);
        await shard.DidNotReceive().RetryJobLaterAsync(
            Arg.Any<IJobRunContext>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await shard.DidNotReceive().RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        async ValueTask<DurableJobRunResult> ExecuteAsync(CancellationToken attemptCancellationToken)
        {
            attemptStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, attemptCancellationToken);
            return DurableJobRunResult.Completed;
        }
    }

    [Fact]
    public async Task RunShardAsync_WhenCompletionLosesOwnership_StopsDispatchingShard()
    {
        var options = CreateOptions(maxConcurrentJobs: 1);
        var shard = CreateJobShard(CreateJobs(2));
        shard.RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(DurableJobMutationResult.OwnershipLost);
        var grainFactory = CreateGrainFactory();
        var extension = Substitute.For<IDurableJobReceiverExtension>();
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(DurableJobRunResult.Completed);
        grainFactory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);
        var services = new ServiceCollection();
        services.AddMetrics();
        using var serviceProvider = services.BuildServiceProvider();
        var meterFactory = serviceProvider.GetRequiredService<IMeterFactory>();
        var instruments = new DurableJobsInstruments(new OrleansInstruments(meterFactory));
        using var shardsProcessed = new MetricCollector<long>(
            meterFactory,
            "Microsoft.Orleans",
            "orleans-durablejobs-shards-processed");
        var executor = new ShardExecutor(
            grainFactory,
            options,
            CreateOverloadDetector(isOverloaded: false),
            NullLogger<ShardExecutor>.Instance,
            durableJobsInstruments: instruments);

        await executor.RunShardAsync(shard, CancellationToken.None);

        await extension.Received(1).HandleDurableJobAsync(
            Arg.Any<IJobRunContext>(),
            Arg.Any<CancellationToken>());
        await shard.Received(1).RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(
            "attempt_canceled",
            Assert.Single(shardsProcessed.GetMeasurementSnapshot()).Tags["status"]);
    }

    [Fact]
    public async Task RunShardAsync_WhenCancellationWinsBeforeAttemptReservation_DoesNotStartHandler()
    {
        var options = CreateOptions(maxConcurrentJobs: 1);
        var shard = CreateJobShard(CreateJobs(1));
        shard.TryStartAttemptAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(DurableJobMutationResult.JobNotFound);
        var grainFactory = CreateGrainFactory();
        var extension = Substitute.For<IDurableJobReceiverExtension>();
        grainFactory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);
        var executor = new ShardExecutor(
            grainFactory,
            options,
            CreateOverloadDetector(isOverloaded: false),
            NullLogger<ShardExecutor>.Instance);

        await executor.RunShardAsync(shard, CancellationToken.None);

        await extension.DidNotReceive().HandleDurableJobAsync(
            Arg.Any<IJobRunContext>(),
            Arg.Any<CancellationToken>());
        await shard.DidNotReceive().RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await shard.DidNotReceive().RetryJobLaterAsync(
            Arg.Any<IJobRunContext>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunShardAsync_RescheduleAtReschedulesWithoutInvokingFailureRetryPolicy()
    {
        var retryPolicyInvoked = false;
        var options = CreateOptions(
            maxConcurrentJobs: 1,
            shouldRetry: (_, _) =>
            {
                retryPolicyInvoked = true;
                return null;
            });
        var shard = CreateJobShard(CreateJobs(1));
        var grainFactory = CreateGrainFactory();
        var rescheduleTime = DateTimeOffset.UtcNow.AddMinutes(5);
        var extension = Substitute.For<IDurableJobReceiverExtension>();
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(DurableJobRunResult.RescheduleAt(rescheduleTime));
        grainFactory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);
        var executor = new ShardExecutor(
            grainFactory,
            options,
            CreateOverloadDetector(isOverloaded: false),
            NullLogger<ShardExecutor>.Instance);

        await executor.RunShardAsync(shard, CancellationToken.None);

        Assert.False(retryPolicyInvoked);
        await shard.Received(1).RescheduleJobAsync(
            Arg.Any<IJobRunContext>(),
            rescheduleTime,
            Arg.Any<CancellationToken>());
        await shard.DidNotReceive().RetryJobLaterAsync(
            Arg.Any<IJobRunContext>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await shard.DidNotReceive().RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(DurableJobMutationResult.JobNotFound)]
    [InlineData(DurableJobMutationResult.OwnershipLost)]
    public async Task RunShardAsync_WhenRetryIsNotApplied_DoesNotRecordRetry(DurableJobMutationResult mutationResult)
    {
        var options = CreateOptions(
            maxConcurrentJobs: 1,
            shouldRetry: (_, _) => DateTimeOffset.UtcNow.AddMinutes(1));
        var shard = CreateJobShard(CreateJobs(1));
        shard.RetryJobLaterAsync(Arg.Any<IJobRunContext>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(mutationResult);
        var grainFactory = CreateGrainFactory();
        var extension = Substitute.For<IDurableJobReceiverExtension>();
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(DurableJobRunResult.Failed(new InvalidOperationException("failed")));
        grainFactory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);
        var services = new ServiceCollection();
        services.AddMetrics();
        using var serviceProvider = services.BuildServiceProvider();
        var meterFactory = serviceProvider.GetRequiredService<IMeterFactory>();
        var instruments = new DurableJobsInstruments(new OrleansInstruments(meterFactory));
        using var retried = new MetricCollector<long>(
            meterFactory,
            "Microsoft.Orleans",
            "orleans-durablejobs-jobs-retried");
        var executor = new ShardExecutor(
            grainFactory,
            options,
            CreateOverloadDetector(isOverloaded: false),
            NullLogger<ShardExecutor>.Instance,
            durableJobsInstruments: instruments);

        await executor.RunShardAsync(shard, CancellationToken.None);

        await shard.Received(1).RetryJobLaterAsync(
            Arg.Any<IJobRunContext>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(retried.GetMeasurementSnapshot());
    }

    [Theory]
    [InlineData(DurableJobMutationResult.JobNotFound)]
    [InlineData(DurableJobMutationResult.OwnershipLost)]
    public async Task RunShardAsync_WhenRescheduleIsNotApplied_DoesNotRecordReschedule(DurableJobMutationResult mutationResult)
    {
        var options = CreateOptions(maxConcurrentJobs: 1);
        var shard = CreateJobShard(CreateJobs(1));
        shard.RescheduleJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(mutationResult);
        var grainFactory = CreateGrainFactory();
        var extension = Substitute.For<IDurableJobReceiverExtension>();
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(DurableJobRunResult.RescheduleAt(DateTimeOffset.UtcNow.AddMinutes(1)));
        grainFactory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);
        var services = new ServiceCollection();
        services.AddMetrics();
        using var serviceProvider = services.BuildServiceProvider();
        var meterFactory = serviceProvider.GetRequiredService<IMeterFactory>();
        var instruments = new DurableJobsInstruments(new OrleansInstruments(meterFactory));
        using var rescheduled = new MetricCollector<long>(
            meterFactory,
            "Microsoft.Orleans",
            "orleans-durablejobs-jobs-rescheduled");
        var executor = new ShardExecutor(
            grainFactory,
            options,
            CreateOverloadDetector(isOverloaded: false),
            NullLogger<ShardExecutor>.Instance,
            durableJobsInstruments: instruments);

        await executor.RunShardAsync(shard, CancellationToken.None);

        await shard.Received(1).RescheduleJobAsync(
            Arg.Any<IJobRunContext>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(rescheduled.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task RunShardAsync_UnknownStatusFlowsThroughFailureRetryPolicy()
    {
        Exception? retryException = null;
        var retryTime = DateTimeOffset.UtcNow.AddMinutes(1);
        var options = CreateOptions(
            maxConcurrentJobs: 1,
            shouldRetry: (_, exception) =>
            {
                retryException = exception;
                return retryTime;
            });
        var shard = CreateJobShard(CreateJobs(1));
        var grainFactory = CreateGrainFactory();
        var extension = Substitute.For<IDurableJobReceiverExtension>();
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(CreateResult((DurableJobRunStatus)99));
        grainFactory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);
        var executor = new ShardExecutor(
            grainFactory,
            options,
            CreateOverloadDetector(isOverloaded: false),
            NullLogger<ShardExecutor>.Instance);

        await executor.RunShardAsync(shard, CancellationToken.None);

        Assert.IsType<InvalidOperationException>(retryException);
        Assert.Contains("unsupported status value 99", retryException.Message, StringComparison.Ordinal);
        await shard.Received(1).RetryJobLaterAsync(Arg.Any<IJobRunContext>(), retryTime, Arg.Any<CancellationToken>());
        await shard.DidNotReceive().RescheduleJobAsync(
            Arg.Any<IJobRunContext>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await shard.DidNotReceive().RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static DurableJobRunResult CreateResult(DurableJobRunStatus status) =>
        (DurableJobRunResult)Activator.CreateInstance(
            typeof(DurableJobRunResult),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [status, null, null, null],
            culture: null)!;

    [Fact]
    public async Task RunShardAsync_WithSlowStart_GraduallyIncreasesConcurrency()
    {
        var initialConcurrency = 2;
        var maxConcurrency = 16;
        var options = CreateOptions(
            maxConcurrentJobs: maxConcurrency,
            concurrencySlowStartEnabled: true,
            slowStartInitialConcurrency: initialConcurrency,
            slowStartInterval: TimeSpan.FromMilliseconds(100));
        var overloadDetector = CreateOverloadDetector(isOverloaded: false);
        var grainFactory = CreateGrainFactory();
        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance);

        var currentConcurrent = 0;
        var maxObservedConcurrent = 0;
        var concurrentLock = new object();
        var releaseJobs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrencyIncreased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // 足够练习慢启动加速的作业
        var jobs = CreateJobs(20);
        var shard = CreateJobShard(jobs);

        ConfigureGrainFactoryWithSlowJobExecution(grainFactory, async () =>
        {
            lock (concurrentLock)
            {
                currentConcurrent++;
                if (currentConcurrent > maxObservedConcurrent)
                {
                    maxObservedConcurrent = currentConcurrent;
                    if (maxObservedConcurrent > initialConcurrency)
                    {
                        concurrencyIncreased.TrySetResult();
                    }
                }
            }

            await releaseJobs.Task;

            lock (concurrentLock)
            {
                currentConcurrent--;
            }
        });

        var runTask = executor.RunShardAsync(shard, CancellationToken.None);
        try
        {
            var completedTask = await Task.WhenAny(
                concurrencyIncreased.Task,
                Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Same(concurrencyIncreased.Task, completedTask);
        }
        finally
        {
            releaseJobs.TrySetResult();
            await runTask;
        }

        // 开始缓慢应该限制初始货币货币, 然后加速
        Assert.True(maxObservedConcurrent <= maxConcurrency,
            $"Max concurrent jobs was {maxObservedConcurrent}, but limit was {maxConcurrency}");
        Assert.True(maxObservedConcurrent > initialConcurrency,
            $"Expected concurrency to increase beyond initial {initialConcurrency}, but max observed was {maxObservedConcurrent}");
    }

    [Fact]
    public async Task RunShardAsync_WithSlowStartDisabled_UsesFullConcurrencyImmediately()
    {
        var maxConcurrency = 5;
        var options = CreateOptions(
            maxConcurrentJobs: maxConcurrency,
            concurrencySlowStartEnabled: false);
        var overloadDetector = CreateOverloadDetector(isOverloaded: false);
        var grainFactory = CreateGrainFactory();
        var executor = new ShardExecutor(grainFactory, options, overloadDetector, NullLogger<ShardExecutor>.Instance);

        var currentConcurrent = 0;
        var maxObservedConcurrent = 0;
        var concurrentLock = new object();

        var jobs = CreateJobs(10);
        var shard = CreateJobShard(jobs);

        ConfigureGrainFactoryWithSlowJobExecution(grainFactory, async () =>
        {
            lock (concurrentLock)
            {
                currentConcurrent++;
                if (currentConcurrent > maxObservedConcurrent)
                {
                    maxObservedConcurrent = currentConcurrent;
                }
            }

            await Task.Delay(100);

            lock (concurrentLock)
            {
                currentConcurrent--;
            }
        });

        await executor.RunShardAsync(shard, CancellationToken.None);

        // 未经缓慢开始,应立即提供所有货币汇率的空缺
        Assert.True(maxObservedConcurrent <= maxConcurrency,
            $"Max concurrent jobs was {maxObservedConcurrent}, but limit was {maxConcurrency}");
        Assert.Equal(maxConcurrency, maxObservedConcurrent);
    }

    [Fact]
    public void ValidateConfiguration_WithSlowStartDisabled_AllowsNonPositiveInitialConcurrency()
    {
        var options = Options.Create(new DurableJobsOptions
        {
            ConcurrencySlowStartEnabled = false,
            SlowStartInitialConcurrency = 0
        });
        var validator = new Orleans.Hosting.DurableJobsOptionsValidator(
            NullLogger<Orleans.Hosting.DurableJobsOptionsValidator>.Instance,
            options);

        validator.ValidateConfiguration();
    }

    // 帮助工具

    private static IOptions<DurableJobsOptions> CreateOptions(
        int maxConcurrentJobs = 10,
        TimeSpan? overloadBackoffDelay = null,
        Func<IJobRunContext, Exception, DateTimeOffset?>? shouldRetry = null,
        bool concurrencySlowStartEnabled = false,
        int? slowStartInitialConcurrency = null,
        TimeSpan? slowStartInterval = null)
    {
        var options = new DurableJobsOptions
        {
            MaxConcurrentJobsPerSilo = maxConcurrentJobs,
            OverloadBackoffDelay = overloadBackoffDelay ?? TimeSpan.FromMilliseconds(100),
            ShouldRetry = shouldRetry ?? ((_, _) => null), // 默认: 没有重试
            ConcurrencySlowStartEnabled = concurrencySlowStartEnabled,
            SlowStartInitialConcurrency = slowStartInitialConcurrency ?? Environment.ProcessorCount,
            SlowStartInterval = slowStartInterval ?? TimeSpan.FromSeconds(10)
        };
        return Options.Create(options);
    }

    private static IOverloadDetector CreateOverloadDetector(bool isOverloaded)
    {
        var detector = Substitute.For<IOverloadDetector>();
        detector.IsOverloaded.Returns(isOverloaded);
        return detector;
    }

    private static List<DurableJob> CreateJobs(int count, DateTimeOffset? dueTime = null)
    {
        var jobs = new List<DurableJob>();
        var baseDueTime = dueTime ?? DateTimeOffset.UtcNow.AddMilliseconds(-100);

        for (int i = 0; i < count; i++)
        {
            jobs.Add(new DurableJob
            {
                Id = $"job-{i}",
                Name = $"job-{i}",
                DueTime = baseDueTime.AddMilliseconds(i * 10),
                TargetGrainId = GrainId.Create("test", $"grain-{i}"),
                ShardId = "shard-1",
                Metadata = null
            });
        }

        return jobs;
    }

    private static IJobShard CreateJobShard(
        List<DurableJob> jobs,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        TaskCompletionSource? firstJobRegistered = null)
    {
        var shard = Substitute.For<IJobShard>();
        shard.Id.Returns("shard-1");
        shard.StartTime.Returns(startTime ?? DateTimeOffset.UtcNow.AddMinutes(-10));
        shard.EndTime.Returns(endTime ?? DateTimeOffset.UtcNow.AddMinutes(10));

        shard.ConsumeDurableJobsAsync().Returns(callInfo => CreateJobContexts(jobs, firstJobRegistered));

        shard.RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DurableJobMutationResult.Applied));
        shard.RetryJobLaterAsync(Arg.Any<IJobRunContext>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DurableJobMutationResult.Applied));
        shard.RescheduleJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DurableJobMutationResult.Applied));

        return shard;
    }

    private static IJobShard CreateJobShardWithDelayedYield(int jobCount, TimeSpan yieldDelay)
    {
        var jobs = CreateJobs(jobCount);
        var shard = Substitute.For<IJobShard>();
        shard.Id.Returns("shard-1");
        shard.StartTime.Returns(DateTimeOffset.UtcNow.AddMinutes(-10));
        shard.EndTime.Returns(DateTimeOffset.UtcNow.AddMinutes(10));

        shard.ConsumeDurableJobsAsync().Returns(callInfo => CreateJobContextsWithDelay(jobs, yieldDelay));

        shard.RemoveJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DurableJobMutationResult.Applied));
        shard.RetryJobLaterAsync(Arg.Any<IJobRunContext>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DurableJobMutationResult.Applied));
        shard.RescheduleJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DurableJobMutationResult.Applied));

        return shard;
    }

    private static async IAsyncEnumerable<IJobRunContext> CreateJobContexts(
        List<DurableJob> jobs,
        TaskCompletionSource? firstJobRegistered = null)
    {
        for (var i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            var context = Substitute.For<IJobRunContext>();
            context.Job.Returns(job);
            context.RunId.Returns(Guid.NewGuid().ToString());
            context.DequeueCount.Returns(1);
            yield return context;

            // 只在 RunShardAsync 登记了所完成的工作任务后,才恢复迭代器。
            if (i == 0)
            {
                firstJobRegistered?.TrySetResult();
            }
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<IJobRunContext> CreateJobContextsWithDelay(
        List<DurableJob> jobs,
        TimeSpan delay)
    {
        foreach (var job in jobs)
        {
            await Task.Delay(delay);

            var context = Substitute.For<IJobRunContext>();
            context.Job.Returns(job);
            context.RunId.Returns(Guid.NewGuid().ToString());
            context.DequeueCount.Returns(1);
            yield return context;
        }
    }

    private static IInternalGrainFactory CreateGrainFactory()
    {
        var factory = Substitute.For<IInternalGrainFactory>();

        var extension = Substitute.For<IDurableJobReceiverExtension>();
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(DurableJobRunResult.Completed);

        factory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);

        return factory;
    }

    private static void ConfigureGrainFactoryToTrackCompletions(
        IInternalGrainFactory factory,
        List<string> completedJobs)
    {
        var extension = Substitute.For<IDurableJobReceiverExtension>();
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var context = callInfo.ArgAt<IJobRunContext>(0);
                lock (completedJobs)
                {
                    completedJobs.Add(context.Job.Id);
                }
                return DurableJobRunResult.Completed;
            });

        factory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);
    }

    private static void ConfigureGrainFactoryWithSlowJobExecution(
        IInternalGrainFactory factory,
        Func<Task> executionAction)
    {
        var extension = Substitute.For<IDurableJobReceiverExtension>();
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns((Func<NSubstitute.Core.CallInfo, ValueTask<DurableJobRunResult>>)(async callInfo =>
            {
                await executionAction();
                return DurableJobRunResult.Completed;
            }));

        factory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);
    }

    private static void ConfigureGrainFactoryWithSelectiveFailures(
        IInternalGrainFactory factory,
        List<string> completedJobs,
        List<string> failedJobs,
        ref int jobExecutionCount)
    {
        var executionCount = jobExecutionCount;

        var extension = Substitute.For<IDurableJobReceiverExtension>();
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var context = callInfo.ArgAt<IJobRunContext>(0);
                var currentExecution = Interlocked.Increment(ref executionCount);

                // 首份工作失败
                if (currentExecution == 1)
                {
                    lock (failedJobs)
                    {
                        failedJobs.Add(context.Job.Id);
                    }
                    var exception = new InvalidOperationException("Simulated job failure");
                    return DurableJobRunResult.Failed(exception);
                }

                // 其他工作成功
                lock (completedJobs)
                {
                    completedJobs.Add(context.Job.Id);
                }
                return DurableJobRunResult.Completed;
            });

        factory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);

        jobExecutionCount = executionCount;
    }

    private static IInternalGrainFactory CreateGrainFactoryWithCanceledExecution()
    {
        var factory = Substitute.For<IInternalGrainFactory>();

        var extension = Substitute.For<IDurableJobReceiverExtension>();
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromCanceled<DurableJobRunResult>(new CancellationToken(canceled: true)));

        factory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);

        return factory;
    }

    private static (IInternalGrainFactory, StrongBox<int>) CreateGrainFactoryWithPollingBehavior()
    {
        var factory = Substitute.For<IInternalGrainFactory>();
        var callBox = new StrongBox<int>(0);

        var extension = Substitute.For<IDurableJobReceiverExtension>();

        // 头3个呼叫返回 InProgress,第4个呼叫返回
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var currentCall = Interlocked.Increment(ref callBox.Value);
                if (currentCall < 4)
                {
                    return DurableJobRunResult.InProgress(TimeSpan.FromMilliseconds(10));
                }
                return DurableJobRunResult.Completed;
            });

        factory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);

        return (factory, callBox);
    }

    private static (IInternalGrainFactory, StrongBox<int>) CreateGrainFactoryWithPollingThenFailure()
    {
        var factory = Substitute.For<IInternalGrainFactory>();
        var callBox = new StrongBox<int>(0);

        var extension = Substitute.For<IDurableJobReceiverExtension>();

        // 头三通呼叫返回 InProgress, 第4次返回失败
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var currentCall = Interlocked.Increment(ref callBox.Value);
                if (currentCall < 4)
                {
                    return DurableJobRunResult.InProgress(TimeSpan.FromMilliseconds(10));
                }
                var exception = new InvalidOperationException("Job failed after polling");
                return DurableJobRunResult.Failed(exception);
            });

        factory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);

        return (factory, callBox);
    }

    private static (IInternalGrainFactory, StrongBox<int>) CreateGrainFactoryWithTimedPolling(
        int pollDelayMs,
        List<DateTimeOffset> pollTimestamps)
    {
        var factory = Substitute.For<IInternalGrainFactory>();
        var callBox = new StrongBox<int>(0);

        var extension = Substitute.For<IDurableJobReceiverExtension>();

        // 头3个呼叫返回 InProgress (初始呼叫后记录时间戳),第4个返回完成
        extension.HandleDurableJobAsync(Arg.Any<IJobRunContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var currentCall = Interlocked.Increment(ref callBox.Value);
                if (currentCall > 1)
                {
                    lock (pollTimestamps)
                    {
                        pollTimestamps.Add(DateTimeOffset.UtcNow);
                    }
                }

                if (currentCall < 4)
                {
                    return DurableJobRunResult.InProgress(TimeSpan.FromMilliseconds(pollDelayMs));
                }
                return DurableJobRunResult.Completed;
            });

        factory.GetGrain<IDurableJobReceiverExtension>(Arg.Any<GrainId>()).Returns(extension);

        return (factory, callBox);
    }

    private sealed class TimerTrackingFakeTimeProvider(DateTimeOffset startDateTime) : FakeTimeProvider(startDateTime)
    {
        private readonly TaskCompletionSource _timerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task TimerCreated => _timerCreated.Task;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            _timerCreated.TrySetResult();
            return timer;
        }
    }
}
