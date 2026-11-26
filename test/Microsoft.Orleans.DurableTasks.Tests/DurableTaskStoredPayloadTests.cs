using System.Reflection;
using Orleans.CodeGeneration;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.Invocation;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 证明一个无法解码的储存有效载荷 和无法存储的结果 只影响他们自己的任务
/// 排 grain 启动、运行其他任务,并不断承诺。
/// </summary>
/// <remarks>测试运行 grain日记上的运行时间,因此每个有效载荷都 Orleans- 与生产时一样序列。</remarks>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class DurableTaskStoredPayloadTests
{
    private static CancellationToken TestCancellation => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(DurableTaskJournalHarness.Formats), MemberType = typeof(DurableTaskJournalHarness))]
    public Task RequestWhichNoLongerDecodesIsQuarantinedUntilCodeDecodesIt(string journalFormatKey) => ActivationScheduler.RunAsync(async () =>
    {
        PoisonedRequest.DecodeFails = false;
        PoisonedRequest.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = new DurableTaskReplayHarness(static _ => DurableTask.FromResult(5), journalFormatKey);
        var poisonedId = TaskId.CreateRoot($"poisoned-{Guid.NewGuid():N}");
        var otherId = TaskId.CreateRoot("other");
        byte[] payload;
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await ((IDurableTaskServer)activation.Runtime).ScheduleAsync(
                poisonedId,
                new PoisonedRequest
                {
                    Context = new DurableTaskRequestContext { TargetId = DurableTaskReplayHarness.GrainId },
                    Poison = new PoisonValue(7),
                },
                TestCancellation);
            Assert.True(activation.Storage.TryGetTask(poisonedId, out var stored));
            payload = ((DurableTaskState)stored).RequestPayload!;
        }

        // 部署无法再解码存储的请求: grain 启动并运行其其他工作,
        // 任务拖拉,其存储的请求不变。
        PoisonedRequest.DecodeFails = true;
        using (var stalls = new StallRecorder(poisonedId))
        {
            await using var activation = await harness.ActivateAsync(TestCancellation);
            var stalled = await stalls.Stalled.WaitAsync(TimeSpan.FromSeconds(5), TestCancellation);
            Assert.Equal(DurableTaskAttemptCancellationReason.Diverged, stalled.Reason);
            Assert.Contains("no longer decodes", stalled.Exception.Message, StringComparison.Ordinal);

            await activation.ScheduleAsync(otherId, TestCancellation);
            Assert.Equal(5, (await activation.GetResponseAsync(otherId, TestCancellation)).GetResult<int>());
            Assert.True(activation.Storage.TryGetTask(poisonedId, out var quarantined));
            Assert.Null(quarantined.CompletedAt);
            Assert.Equal(payload, ((DurableTaskState)quarantined).RequestPayload);
            Assert.Equal(
                DurableTaskStatus.Pending,
                (await activation.Runtime.SubscribeOrPollAsync(poisonedId, new SubscribeOrPollOptions { PollTimeout = TimeSpan.Zero }, TestCancellation)).Status);
        }

        // 用于解码请求的代码运行任务, 而不是存储的请求 。
        PoisonedRequest.DecodeFails = false;
        PoisonedRequest.Release.SetResult();
        await using var recovered = await harness.ActivateAsync(TestCancellation);
        Assert.Equal(7, (await recovered.GetResponseAsync(poisonedId, TestCancellation)).GetResult<int>());
        Assert.Equal(5, (await recovered.GetResponseAsync(otherId, TestCancellation)).GetResult<int>());
    });

    [Theory]
    [MemberData(nameof(DurableTaskJournalHarness.Formats), MemberType = typeof(DurableTaskJournalHarness))]
    public Task ResultWhichDoesNotSerializeFaultsItsTaskAndLaterWorkCommits(string journalFormatKey) => ActivationScheduler.RunAsync(async () =>
    {
        await using var harness = new DurableTaskReplayHarness(static _ => DurableTask.FromResult(new NoCodecValue()), journalFormatKey);
        await AssertFaultsAndLaterWorkCommitsAsync(harness, "result", typeof(NoCodecValue));
    });

    [Theory]
    [MemberData(nameof(DurableTaskJournalHarness.Formats), MemberType = typeof(DurableTaskJournalHarness))]
    public Task ExceptionWhichDoesNotSerializeFaultsItsTaskAndLaterWorkCommits(string journalFormatKey) => ActivationScheduler.RunAsync(async () =>
    {
        await using var harness = new DurableTaskReplayHarness(static _ => DurableTask.Run(static _ => ThrowUnserializable()), journalFormatKey);
        await AssertFaultsAndLaterWorkCommitsAsync(harness, "exception", typeof(InvalidOperationException));

        static int ThrowUnserializable() =>
            throw new InvalidOperationException("The order was rejected.") { Data = { ["order"] = new NoCodecValue() } };
    });

    /// <summary>
    /// 运行着操纵系统的工作流程, 完成的结果不连续排列, 并声称它的任务
    /// 长期缺损, grain 以后的工作承诺。
    /// </summary>
    private static async Task AssertFaultsAndLaterWorkCommitsAsync(DurableTaskReplayHarness harness, string kind, Type type)
    {
        var faultedId = TaskId.CreateRoot("faulted");
        var laterId = TaskId.CreateRoot("later");
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(faultedId, TestCancellation);
            AssertSerializationFault(await activation.GetResponseAsync(faultedId, TestCancellation));

            harness.Workflow = static _ => DurableTask.FromResult(5);
            await activation.ScheduleAsync(laterId, TestCancellation);
            Assert.Equal(5, (await activation.GetResponseAsync(laterId, TestCancellation)).GetResult<int>());
        }

        // 错误像任何结果一样发生, 所以下一次激活会恢复它而不运行工作流程。
        await using var recovered = await harness.ActivateAsync(TestCancellation);
        AssertSerializationFault(await recovered.GetResponseAsync(faultedId, TestCancellation));
        Assert.Equal(5, (await recovered.GetResponseAsync(laterId, TestCancellation)).GetResult<int>());

        void AssertSerializationFault(DurableTaskResponse response)
        {
            Assert.Equal(DurableTaskStatus.Failed, response.Status);
            var exception = Assert.IsType<SerializerException>(response.Exception);
            Assert.Contains($"The {kind} of durable task '{faultedId}', of type '{type}', can't be stored because it doesn't serialize", exception.Message, StringComparison.Ordinal);
        }
    }
}

/// <summary>一种没有编码的值,所以不序列。</summary>
internal sealed class NoCodecValue;

/// <summary>数值,该值的解码在 <see cref="PoisonedRequest.DecodeFails"/> 被设置时发生故障,而已删除的类型则被设定。</summary>
internal sealed class PoisonValue(int value)
{
    public int Value { get; } = value;
}

[GenerateSerializer]
internal struct PoisonValueSurrogate
{
    [Id(0)]
    public int Value;
}

[RegisterConverter]
internal sealed class PoisonValueConverter : IConverter<PoisonValue, PoisonValueSurrogate>
{
    public PoisonValue ConvertFromSurrogate(in PoisonValueSurrogate surrogate) =>
        PoisonedRequest.DecodeFails ? throw new InvalidOperationException("The poisoned value's type was removed.") : new(surrogate.Value);

    public PoisonValueSurrogate ConvertToSurrogate(in PoisonValue value) => new() { Value = value.Value };
}

/// <summary>其论据仅在时间解码的请求 <see cref="DecodeFails"/> 确是明确的,身体等待着的, <see cref="Release"/>.</summary>
[GenerateSerializer]
[Alias("Microsoft.Orleans.DurableTasks.Tests.PoisonedRequest")]
internal sealed class PoisonedRequest : IDurableTaskRequest
{
    /// <summary>获取或设置请求解码失败与否。 只有 <see cref="DurableTaskStoredPayloadTests"/> 使用它。</summary>
    public static bool DecodeFails { get; set; }

    /// <summary>获取或设置请求方尸体等待的大门。</summary>
    public static TaskCompletionSource Release { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Id(0)]
    public DurableTaskRequestContext? Context { get; set; }

    [Id(1)]
    public PoisonValue? Poison { get; set; }

    [Id(2)]
    public InvokeMethodOptions Options { get; private set; }

    public void AddInvokeMethodOptions(InvokeMethodOptions options) => Options |= options;

    public object? GetTarget() => null;

    public void SetTarget(ITargetHolder holder)
    {
    }

    public ValueTask<Response> Invoke() => throw new NotSupportedException("Durable task requests are not invoked directly.");

    public int GetArgumentCount() => 0;

    public object? GetArgument(int index) => throw new ArgumentOutOfRangeException(nameof(index));

    public void SetArgument(int index, object value) => throw new ArgumentOutOfRangeException(nameof(index));

    public string GetMethodName() => "Run";

    public string GetInterfaceName() => "Microsoft.Orleans.DurableTasks.Tests.IPoisonedGrain";

    public string GetActivityName() => "Microsoft.Orleans.DurableTasks.Tests.IPoisonedGrain/Run";

    public MethodInfo GetMethod() => typeof(PoisonedRequest).GetMethod(nameof(GetMethod))!;

    public Type GetInterfaceType() => typeof(PoisonedRequest);

    public void Dispose()
    {
    }

    public DurableTask CreateTask()
    {
        var value = Poison!.Value;
        return DurableTask.Run(async cancellationToken =>
        {
            await Release.Task.WaitAsync(cancellationToken);
            return value;
        });
    }
}
