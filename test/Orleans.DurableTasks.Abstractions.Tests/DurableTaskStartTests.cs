using CsCheck;
using Orleans.Durable.Tasks;
using Xunit;

namespace Orleans.Durable.Tasks.Abstractions.Tests;

[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
[TestCategory("BVT")]
public class DurableTaskStartTests
{
    private static CancellationToken TestCancellation => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task ScheduleOutsideAnExecutionUsesTheNameAsTheRootId()
    {
        var definition = new RootDefinition();

        var scheduled = await definition.WithId("order-42").ScheduleAsync(TestCancellation);

        Assert.Equal(TaskId.CreateRoot("order-42"), scheduled.Id);
        Assert.Equal([TaskId.CreateRoot("order-42")], definition.Scheduled);
    }

    [Fact]
    public async Task StartsOutsideAnExecutionWithoutANameReceiveNewRandomRootIds()
    {
        var definition = new RootDefinition();

        var first = await definition.ScheduleAsync(TestCancellation);
        var second = await definition.ScheduleAsync(TestCancellation);
        await definition;

        Assert.Equal(3, definition.Scheduled.Count);
        Assert.Equal(definition.Scheduled[0], first.Id);
        Assert.Equal(definition.Scheduled[1], second.Id);
        Assert.Equal(3, definition.Scheduled.Distinct().Count());
        Assert.All(definition.Scheduled, static taskId =>
        {
            Assert.Equal(TaskId.None, taskId.Parent());
            Assert.True(Guid.TryParseExact(taskId.ToString(), "N", out _));
        });
    }

    [Fact]
    public async Task DetachedOutsideAnExecutionLeavesTheConfigurationUnchanged()
    {
        var definition = new RootDefinition();

        var named = await definition.Detached().WithId("named").ScheduleAsync(TestCancellation);
        var renamed = await definition.WithId("renamed").Detached().ScheduleAsync(TestCancellation);
        var rooted = await definition.Detached("rooted").ScheduleAsync(TestCancellation);

        Assert.Equal(TaskId.CreateRoot("named"), named.Id);
        Assert.Equal(TaskId.CreateRoot("renamed"), renamed.Id);
        Assert.Equal(TaskId.CreateRoot("rooted"), rooted.Id);
    }

    [Fact]
    public void PostOutsideAnExecutionStagesTheRootThroughTheDefinition()
    {
        var definition = new RootDefinition();

        var named = definition.WithId("named").Post();
        var unnamed = definition.Post();

        Assert.Equal(TaskId.CreateRoot("named"), named.Id);
        Assert.Equal([TaskId.CreateRoot("named"), unnamed.Id], definition.Posted);
        Assert.Empty(definition.Scheduled);
    }

    [Fact]
    public void PostOutsideAnExecutionRequiresADurableOutbox()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new ScheduleOnlyDefinition().WithId("root").Post());
        Assert.Contains("durable outbox", exception.Message, StringComparison.Ordinal);

        exception = Assert.Throws<InvalidOperationException>(() => DurableTask.FromResult(1).WithId("root").Post());
        Assert.Contains("root scheduling", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartsWithinAnExecutionPassTheirOptionsToTheHost()
    {
        var definition = new RootDefinition();
        var context = new StartRecordingContext(TaskId.CreateRoot("starter"));

        var response = await RunAsync(context, async () =>
        {
            await definition.WithId("awaited");
            _ = await definition.WithId("scheduled").ScheduleAsync();
            _ = definition.WithId("posted").Post();
        });

        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.Equal(
            [
                ("start", TaskId.Parse("starter/awaited"), DurableTaskStartOptions.None),
                ("start", TaskId.Parse("starter/scheduled"), DurableTaskStartOptions.WaitForAcceptance),
                ("post", TaskId.Parse("starter/posted"), DurableTaskStartOptions.None),
            ],
            context.Starts.Select(static start => (start.Kind, start.TaskId, start.Options)));
        Assert.All(context.Starts, start => Assert.Same(definition, start.Definition));
        Assert.Empty(definition.Scheduled);
    }

    [Fact]
    public async Task DetachedTasksWithinAnExecutionUseASuppliedRootIdVerbatim()
    {
        var definition = new RootDefinition();
        var context = new StartRecordingContext(TaskId.CreateRoot("starter"));

        var response = await RunAsync(context, async () =>
        {
            await definition.Detached("place-order-42");
            _ = await definition.Detached("place-order-42").ScheduleAsync();
            _ = definition.Detached("tenant/order$1").Post();
            await definition;
        });

        // 提供根标识符时没有执行的识别符号, 因此以下未命名的孩子是第一个 。
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.Equal(
            [
                ("start", TaskId.CreateRoot("place-order-42"), DurableTaskStartOptions.Detached),
                ("start", TaskId.CreateRoot("place-order-42"), DurableTaskStartOptions.Detached | DurableTaskStartOptions.WaitForAcceptance),
                ("post", TaskId.CreateRoot("tenant/order$1"), DurableTaskStartOptions.Detached),
                ("start", TaskId.Parse("starter/$child-1"), DurableTaskStartOptions.None),
            ],
            context.Starts.Select(static start => (start.Kind, start.TaskId, start.Options)));
    }

    [Fact]
    public async Task NamedDetachedTasksDeriveTheirRootIdsFromTheNamedOperationInAnyConfigurationOrder()
    {
        var definition = new RootDefinition();
        var context = new StartRecordingContext(TaskId.CreateRoot("starter"));

        var response = await RunAsync(context, async () =>
        {
            await definition.Detached().WithId("receipt");
            await definition.WithId("receipt").Detached();
            await definition.WithId("receipt");
        });

        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.Equal(
            [
                TaskId.CreateRoot("starter/receipt"),
                TaskId.CreateRoot("starter/receipt$2"),
                TaskId.Parse("starter/receipt$3"),
            ],
            context.Starts.Select(static start => start.TaskId));
    }

    [Fact]
    public async Task ConfiguringANameAllocatesNoIdentifierUntilTheTaskStarts()
    {
        var definition = new RootDefinition();
        var context = new StartRecordingContext(TaskId.CreateRoot("starter"));

        var response = await RunAsync(context, async () =>
        {
            var unused = definition.WithId("charge");
            var configured = definition.WithId("charge");
            var copy = configured;
            await configured;
            await copy;
            GC.KeepAlive(unused);
            Assert.Throws<InvalidOperationException>(() => copy.WithId("renamed"));
            Assert.Throws<InvalidOperationException>(() => copy.Detached("renamed"));
        });

        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.Equal(
            [TaskId.Parse("starter/charge"), TaskId.Parse("starter/charge$2")],
            context.Starts.Select(static start => start.TaskId));
    }

    [Fact]
    public async Task UnnamedDetachedTasksDeriveReplayStableRootIdsFromTheUnnamedOperationCount()
    {
        var definition = new RootDefinition();
        async Task<StartRecordingContext> RunOnceAsync()
        {
            var context = new StartRecordingContext(TaskId.Parse("order-42/charge"));
            var response = await RunAsync(context, async () =>
            {
                await definition;
                await definition.Detached();
                _ = definition.Detached().Post();
            });
            Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
            return context;
        }

        var first = await RunOnceAsync();
        var replay = await RunOnceAsync();

        Assert.Equal(
            [
                TaskId.Parse("order-42/charge/$child-1"),
                TaskId.CreateRoot("order-42/charge/$child-2"),
                TaskId.CreateRoot("order-42/charge/$child-3"),
            ],
            first.Starts.Select(static start => start.TaskId));
        Assert.Equal(first.Starts.Select(static start => start.TaskId), replay.Starts.Select(static start => start.TaskId));
    }

    [Fact]
    public async Task HostIdentityQualifiesTheDerivedRootIdsOfDetachedTasks()
    {
        var definition = new RootDefinition();
        var context = new StartRecordingContext(TaskId.Parse("order-42/charge"), hostId: "order/42");

        var response = await RunAsync(context, async () =>
        {
            await definition.Detached();
            await definition.Detached().WithId("receipt");
            await definition.Detached("receipt");
        });

        // 在不同主机上使用相同识别符的执行结果产生不同的根标识符,而提供根
        // 每个主机都使用相同的识别符号。主机的身份会逃避它的“ / ” 。
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.Equal(
            [
                TaskId.CreateRoot(@"order\/42/order-42/charge/$child-1"),
                TaskId.CreateRoot(@"order\/42/order-42/charge/receipt"),
                TaskId.CreateRoot("receipt"),
            ],
            context.Starts.Select(static start => start.TaskId));
    }

    [Fact]
    public void DistinctHostsExecutionsAndOperationsDeriveDistinctRootIds()
    {
        var segment = Gen.Char['!', '~'].Array[1, 4].Select(static chars => new string(chars).Replace("$", "-", StringComparison.Ordinal));
        var input = Gen.Select(segment.Array[0, 2], segment.Array[1, 3], segment);
        Gen.Select(input, input).Sample(static (first, second) =>
        {
            var firstRoot = DeriveRoot(first);
            var secondRoot = DeriveRoot(second);
            var same = Equivalent(first, second);
            return same ? firstRoot == secondRoot : firstRoot != secondRoot;
        });

        static TaskId DeriveRoot((string[] Host, string[] Execution, string Name) input)
        {
            var context = new StartRecordingContext(
                TaskId.Parse(string.Join('/', input.Execution.Select(static segment => TaskId.CreateRoot(segment).ToString()))),
                hostId: input.Host.Length == 0 ? null : string.Join('/', input.Host));
            var definition = new RootDefinition();
            var response = RunAsync(context, async () => await definition.Detached().WithId(input.Name)).GetAwaiter().GetResult();
            Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
            return Assert.Single(context.Starts).TaskId;
        }

        static bool Equivalent((string[] Host, string[] Execution, string Name) left, (string[] Host, string[] Execution, string Name) right)
            => string.Join('/', left.Host) == string.Join('/', right.Host)
                && (left.Host.Length == 0) == (right.Host.Length == 0)
                && left.Execution.SequenceEqual(right.Execution)
                && left.Name == right.Name;
    }

    [Fact]
    public async Task HandlesNameTheTasksWhichTheirHostsStore()
    {
        var definition = new RootDefinition();
        var context = new StartRecordingContext(TaskId.CreateRoot("starter"), targetTaskId: TaskId.CreateRoot("stored"));
        var plain = new StartRecordingContext(TaskId.CreateRoot("starter"));
        (TaskId Id, TaskId TargetTaskId)[] handles = [];

        await RunAsync(context, async () =>
        {
            var scheduled = await definition.Detached("scheduled").ScheduleAsync();
            var posted = definition.Detached("posted").Post();
            handles = [(scheduled.Id, scheduled.TargetTaskId), (posted.Id, posted.TargetTaskId)];
        });
        await RunAsync(plain, async () =>
        {
            var scheduled = await definition.Detached("scheduled").ScheduleAsync();
            handles = [.. handles, (scheduled.Id, scheduled.TargetTaskId)];
        });

        // 手控器指定其主控件仓库的任务, 以及将任务存储在该手柄识别符下的主机
        // 那个名字
        Assert.Equal(
            [
                (TaskId.CreateRoot("scheduled"), TaskId.CreateRoot("stored")),
                (TaskId.CreateRoot("posted"), TaskId.CreateRoot("stored")),
                (TaskId.CreateRoot("scheduled"), TaskId.CreateRoot("scheduled")),
            ],
            handles);
    }

    [Fact]
    public async Task HandlesExposeTheFeaturesWhichTheirHostsProvide()
    {
        var definition = new RootDefinition();
        var context = new StartRecordingContext(TaskId.CreateRoot("starter"), targetTaskId: TaskId.CreateRoot("stored"));
        var plain = new StartRecordingContext(TaskId.CreateRoot("starter"));
        (HostLocation? Scheduled, HostLocation? Posted, string? Unprovided, HostLocation? Plain) features = default;

        await RunAsync(context, async () =>
        {
            var scheduled = await definition.Detached("scheduled").ScheduleAsync();
            var posted = definition.Detached("posted").Post();
            features = (scheduled.GetFeature<HostLocation>(), posted.GetFeature<HostLocation>(), scheduled.GetFeature<string>(), null);
        });
        await RunAsync(plain, async () =>
        {
            var scheduled = await definition.Detached("scheduled").ScheduleAsync();
            features.Plain = scheduled.GetFeature<HostLocation>();
        });

        Assert.Equal(new HostLocation("host-1"), features.Scheduled);
        Assert.Equal(new HostLocation("host-1"), features.Posted);
        Assert.Null(features.Unprovided);
        Assert.Null(features.Plain);
    }

    [Fact]
    public async Task DetachedStartsRequireADefinitionWhichAHostStartsAsARootTask()
    {
        var context = new StartRecordingContext(TaskId.CreateRoot("starter"));
        var failures = new List<InvalidOperationException>();

        var response = await RunAsync(context, async () =>
        {
            failures.Add(await Assert.ThrowsAsync<InvalidOperationException>(async () => await DurableTask.Run(static _ => 1).Detached()));
            failures.Add(Assert.Throws<InvalidOperationException>(() => DurableTask.Run(static _ => 1).Detached("survey").Post()));
            failures.Add(await Assert.ThrowsAsync<InvalidOperationException>(() => LocalStep().WithId("survey").Detached().ScheduleAsync(TestCancellation)));
            await new RootDefinition().Detached();
        });

        // 被拒绝的启动没有使用识别符号, 因此以下的分离根任务产生第一个。
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.All(failures, static failure => Assert.Contains("ORLEANS0228", failure.Message, StringComparison.Ordinal));
        Assert.Equal([TaskId.CreateRoot("starter/$child-1")], context.Starts.Select(static start => start.TaskId));

        static async DurableTask<int> LocalStep() => 1;
    }

    [Fact]
    public async Task DurableOperationsOffTheExecutionsSchedulerThrow()
    {
        var context = new StartRecordingContext(TaskId.CreateRoot("starter"));
        var scheduler = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;
        var failures = new List<Exception>();

        var response = await RunAsync(context, async () =>
        {
            // 代码在另一个调度器上继续,如 ConfigureAwait(假)之后,无法命令或记录操作。
            await Task.Factory.StartNew(
                () =>
                {
                    failures.Add(Assert.Throws<InvalidOperationException>(() => DurableTask.RunInline("effect", static () => 1)));
                    failures.Add(Assert.Throws<InvalidOperationException>(() => new RootDefinition().WithId("child").Post()));
                    failures.Add(Assert.Throws<InvalidOperationException>(() => new RootDefinition().Post()));
                },
                TestCancellation,
                TaskCreationOptions.None,
                scheduler);
            await new RootDefinition().WithId("child");
        });

        // 拒绝的操作没有使用任何识别资料或发生事件。
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.Equal(3, failures.Count);
        Assert.All(failures, static failure => Assert.Contains("ConfigureAwait(false)", failure.Message, StringComparison.Ordinal));
        Assert.Equal([TaskId.Parse("starter/child")], context.Starts.Select(static start => start.TaskId));
    }

    [Fact]
    public async Task PollsWithinAnExecutionThrow()
    {
        var context = new StartRecordingContext(TaskId.CreateRoot("starter"));
        var definition = new RootDefinition();
        var polls = new List<Exception>();

        var response = await RunAsync(context, async () =>
        {
            var scheduled = await definition.WithId("child").ScheduleAsync();
            polls.Add(await Assert.ThrowsAsync<NotSupportedException>(() => scheduled.IsCompletedAsync(cancellationToken: TestCancellation)));
            polls.Add(await Assert.ThrowsAsync<NotSupportedException>(() => scheduled.GetStatusAsync(cancellationToken: TestCancellation)));
            polls.Add(await Assert.ThrowsAsync<NotSupportedException>(() => scheduled.GetResponseAsync(default(PollingOptions), TestCancellation)));
            await scheduled;
        });

        // 民意测验观察任务运行时的状况, 因此重播可以不同地进行; 尸体等待着。
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.Equal(3, polls.Count);
        Assert.Single(context.Starts);
    }

    [Fact]
    public async Task PostWithinAnExecutionRequiresHostSupport()
    {
        var context = new StartRecordingContext(TaskId.CreateRoot("starter"), supportsPost: false);

        var response = await RunAsync(context, () =>
        {
            _ = new RootDefinition().Post();
            return Task.CompletedTask;
        });

        Assert.Equal(DurableTaskStatus.Failed, response.Status);
        Assert.IsType<NotSupportedException>(response.Exception);
    }

    private static Task<DurableTaskResponse> RunAsync(StartRecordingContext context, Func<Task> body)
        => DurableTaskRuntimeHelper.RunAsync(DurableTask.Run(_ => body()), context).AsTask();

    private sealed class StartRecordingContext(
        TaskId taskId,
        bool supportsPost = true,
        string? hostId = null,
        TaskId? targetTaskId = null) : DurableTaskContext(taskId)
    {
        protected override string? HostId => hostId;

        public List<(string Kind, TaskId TaskId, DurableTask Definition, DurableTaskStartOptions Options)> Starts { get; } = [];

        public override DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;

        public override Guid Incarnation => Guid.Empty;

        protected override ValueTask<IScheduledTaskHandle> StartTaskAsync(
            TaskId taskId,
            DurableTask taskDefinition,
            DurableTaskStartOptions options,
            CancellationToken cancellationToken)
        {
            Starts.Add(("start", taskId, taskDefinition, options));
            return new(CreateHandle(taskId));
        }

        protected override IScheduledTaskHandle PostTask(TaskId taskId, DurableTask taskDefinition, DurableTaskStartOptions options)
        {
            if (!supportsPost)
            {
                return base.PostTask(taskId, taskDefinition, options);
            }

            Starts.Add(("post", taskId, taskDefinition, options));
            return CreateHandle(taskId);
        }

        private IScheduledTaskHandle CreateHandle(TaskId taskId) =>
            targetTaskId is { } target ? new TargetedHandle(taskId, target) : new CompletedHandle(taskId);

        protected override ValueTask<DurableTaskResponse> ScheduleDelayAsync(
            TaskId taskId,
            DateTimeOffset dueTime,
            CancellationToken cancellationToken) => throw new NotSupportedException();


        protected override ValueTask<TaskId> SelectCompletionAsync(
            TaskId decisionId,
            IReadOnlyList<TaskId> candidates,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CompletedHandle(TaskId taskId) : IScheduledTaskHandle
    {
        public TaskId TaskId => taskId;

        public ValueTask<DurableTaskResponse> WaitAsync(CancellationToken cancellationToken) => new(DurableTaskResponse.Completed);

        public ValueTask<DurableTaskResponse> PollAsync(PollingOptions options, CancellationToken cancellationToken) => new(DurableTaskResponse.Completed);

        public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class TargetedHandle(TaskId taskId, TaskId targetTaskId) : IScheduledTaskHandle
    {
        public TaskId TaskId => taskId;

        public TaskId TargetTaskId => targetTaskId;

        public TFeature? GetFeature<TFeature>()
            where TFeature : class => new HostLocation("host-1") as TFeature;

        public ValueTask<DurableTaskResponse> WaitAsync(CancellationToken cancellationToken) => new(DurableTaskResponse.Completed);

        public ValueTask<DurableTaskResponse> PollAsync(PollingOptions options, CancellationToken cancellationToken) => new(DurableTaskResponse.Completed);

        public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed record HostLocation(string Host);

    /// <summary>根定义,其主机在没有耐久发件箱的情况下接受启动。</summary>
    private class ScheduleOnlyDefinition : DurableTask, ISchedulableTask
    {
        public List<TaskId> Scheduled { get; } = [];

        public ValueTask<DurableTaskResponse> ScheduleAsync(TaskId taskId, CancellationToken cancellationToken)
        {
            Scheduled.Add(taskId);
            return new(DurableTaskResponse.Completed);
        }

        public IScheduledTaskHandle GetHandle(TaskId taskId) => new CompletedHandle(taskId);

        protected override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context)
            => throw new NotSupportedException();
    }

    private sealed class RootDefinition : ScheduleOnlyDefinition, ISchedulableTask
    {
        public List<TaskId> Posted { get; } = [];

        IScheduledTaskHandle ISchedulableTask.Post(TaskId taskId)
        {
            Posted.Add(taskId);
            return new CompletedHandle(taskId);
        }
    }
}
