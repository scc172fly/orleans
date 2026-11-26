using CsCheck;
using Orleans.Durable.Tasks;
using Xunit;

namespace Orleans.Durable.Tasks.Abstractions.Tests;

/// <summary>
/// 由生成的物体对生成的物体进行验证, 重播放时不运行功能返回已记录的效果, 即指定每个
/// 操作第一个运行指定的标识符。
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

    /// <summary>生成体的一个操作; 效果的旗帜添加嵌套效应和被拒绝的持久操作。</summary>
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

                // 运行的每个操作都有自己的识别码;一个提供根标识符,在全球范围命名一个任务。
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

    /// <summary>重放的物体不运行效果,所以它不会使用标识符。</summary>
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

    /// <summary>即时完成每次操作、记录效果和列出运行用途的识别符号的主机。</summary>
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

    /// <summary>主机开始作为根任务的定义, 如耐用 grain 调用。</summary>
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
