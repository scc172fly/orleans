using CsCheck;
using Orleans.Durable.Tasks;
using Xunit;

namespace Orleans.Durable.Tasks.Abstractions.Tests;

/// <summary>
/// 通过生成的任务体验证：重放直接返回记录的效果结果而不运行效果，并为每个
/// 操作分配与首次运行相同的标识符。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
[TestCategory("BVT")]
public sealed class ReplayIdentityPropertyTests
{
    private static readonly string[] Names = ["a", "b", "c"];

    private enum Kind
    {
        NamedChild,
        UnnamedChild,
        NamedEffect,
        UnnamedEffect,
        WhenAll,
        UnnamedDetachedPost,
        NamedDetachedPost,
        RootedDetachedPost,
        Version,
    }

    /// <summary>生成的任务体中的一个操作；效果的标志会添加一个嵌套效果和一个被拒绝的持久化操作。</summary>
    private sealed record Operation(Kind Kind, int Name, int Width, bool NestedEffect, bool NestedOperation)
    {
        public override string ToString() => $"{Kind}({Names[Name]}, {Width}, {NestedEffect}, {NestedOperation})";
    }

    [Fact]
    public void ReplayAssignsTheIdentifiersOfTheFirstRun()
    {
        var operation = Gen.Select(
            Gen.Enum<Kind>(),
            Gen.Int[0, Names.Length - 1],
            Gen.Int[1, 3],
            Gen.Bool,
            Gen.Bool,
            static (kind, name, width, nestedEffect, nestedOperation) => new Operation(kind, name, width, nestedEffect, nestedOperation));

        operation.Array[0, 12].Sample(
            static operations =>
            {
                var records = new Dictionary<TaskId, DurableTaskResponse>();
                var first = Run(operations, records);
                var replay = Run(operations, records);

                Assert.Equal(first.Identifiers, replay.Identifiers);
                Assert.Equal(0, replay.EffectRuns);

                // 一次运行中的每个操作都有自己的标识符；显式提供的根标识符在全局标识一个任务。
                var owned = first.Identifiers.Where(static entry => entry.Kind is not ("rooted" or "version")).Select(static entry => entry.Id).ToArray();
                Assert.Equal(owned.Length, owned.Distinct().Count());
            },
            iter: 200,
            threads: 1,
            print: static operations => string.Join(", ", operations.Select(static operation => operation.ToString())));
    }

    private static RecordingContext Run(Operation[] operations, Dictionary<TaskId, DurableTaskResponse> records)
    {
        var context = new RecordingContext(records);
        var response = DurableTaskRuntimeHelper.RunAsync(DurableTask.Run(_ => BodyAsync(operations)), context).AsTask().GetAwaiter().GetResult();
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        return context;
    }

    private static async Task BodyAsync(Operation[] operations)
    {
        foreach (var operation in operations)
        {
            var name = Names[operation.Name];
            switch (operation.Kind)
            {
                case Kind.NamedChild:
                    await DurableTask.Run(static _ => 0).WithId(name);
                    break;
                case Kind.UnnamedChild:
                    await DurableTask.Run(static _ => 0);
                    break;
                case Kind.NamedEffect:
                    DurableTask.RunInline(name, () => Effect(operation));
                    break;
                case Kind.UnnamedEffect:
                    DurableTask.RunInline(() => Effect(operation));
                    break;
                case Kind.WhenAll:
                    await DurableTask.WhenAll(Enumerable.Range(0, operation.Width).Select(static _ => DurableTask.Run(static _ => 0)));
                    break;
                case Kind.UnnamedDetachedPost:
                    _ = new RootCall().Detached().Post();
                    break;
                case Kind.NamedDetachedPost:
                    _ = new RootCall().WithId(name).Detached().Post();
                    break;
                case Kind.RootedDetachedPost:
                    _ = new RootCall().Detached($"root-{name}").Post();
                    break;
                case Kind.Version:
                    _ = DurableTask.GetVersion(name, 0, 1);
                    break;
            }
        }
    }

    /// <summary>一种效果，其操作体不会在重放时运行，因此它执行的任何操作都不得占用标识符。</summary>
    private static int Effect(Operation operation)
    {
        var result = 1;
        if (operation.NestedEffect)
        {
            result += DurableTask.RunInline(static () => 1) + DurableTask.RunInline(Names[operation.Name], static () => 1);
        }

        if (operation.NestedOperation)
        {
            Assert.Throws<InvalidOperationException>(static () => DurableTask.Run(static _ => 0).Post());
            Assert.Throws<InvalidOperationException>(() => DurableTask.GetVersion(Names[operation.Name], 0, 1));
        }

        return result;
    }

    /// <summary>立即完成每个操作、记录效果并列出一次运行所用标识符的宿主。</summary>
    private sealed class RecordingContext(Dictionary<TaskId, DurableTaskResponse> records) : DurableTaskContext(TaskId.CreateRoot("root"))
    {
        public List<(string Kind, string Id)> Identifiers { get; } = [];

        public int EffectRuns { get; private set; }

        public override DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;

        public override Guid Incarnation => Guid.Empty;

        protected override ValueTask<IScheduledTaskHandle> StartTaskAsync(
            TaskId taskId,
            DurableTask taskDefinition,
            DurableTaskStartOptions options,
            CancellationToken cancellationToken)
        {
            Identifiers.Add(("start", taskId.ToString()));
            return new(new CompletedHandle(taskId));
        }

        protected override IScheduledTaskHandle PostTask(TaskId taskId, DurableTask taskDefinition, DurableTaskStartOptions options)
        {
            Identifiers.Add((taskId.ToString().StartsWith("root-", StringComparison.Ordinal) ? "rooted" : "post", taskId.ToString()));
            return new CompletedHandle(taskId);
        }

        protected override DurableTaskResponse RunEffect<TState>(TaskId effectId, TState state, Func<TState, DurableTaskResponse> effect)
        {
            Identifiers.Add(("effect", effectId.ToString()));
            if (!records.TryGetValue(effectId, out var outcome))
            {
                EffectRuns++;
                records[effectId] = outcome = effect(state);
            }

            return outcome;
        }

        protected override int GetVersion(string name, int minSupported, int maxSupported)
        {
            Identifiers.Add(("version", name));
            return maxSupported;
        }

        protected override ValueTask<DurableTaskResponse> ScheduleDelayAsync(TaskId taskId, DateTimeOffset dueTime, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        protected override ValueTask<TaskId> SelectCompletionAsync(TaskId decisionId, IReadOnlyList<TaskId> candidates, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    /// <summary>宿主作为根任务启动的定义，例如持久化 Grain 调用。</summary>
    private sealed class RootCall : DurableTask, ISchedulableTask
    {
        public ValueTask<DurableTaskResponse> ScheduleAsync(TaskId taskId, CancellationToken cancellationToken) => new(DurableTaskResponse.Completed);

        public IScheduledTaskHandle GetHandle(TaskId taskId) => new CompletedHandle(taskId);

        protected override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context) => throw new NotSupportedException();
    }

    private sealed class CompletedHandle(TaskId taskId) : IScheduledTaskHandle
    {
        public TaskId TaskId => taskId;

        public ValueTask<DurableTaskResponse> WaitAsync(CancellationToken cancellationToken) => new(DurableTaskResponse.FromResult(0));

        public ValueTask<DurableTaskResponse> PollAsync(PollingOptions options, CancellationToken cancellationToken)
            => new(DurableTaskResponse.FromResult(0));

        public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
