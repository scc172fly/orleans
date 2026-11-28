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
/// 验证无法解码的已存储有效载荷以及无法存储的结果仅影响各自所属的任务：
/// Grain 仍能激活、运行其他任务并继续提交更改。
/// </summary>
/// <remarks>测试在 Grain 的日志上运行运行时，因此每个有效载荷均像生产环境一样经过 Orleans 序列化。</remarks>
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

        // 部署后的代码无法再解码已存储的请求：Grain 仍能激活并运行其他工作，
        // 而该任务停滞，且其已存储的请求保持不变。
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

        // 能够解码请求的代码会从已存储的请求运行任务。
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
    /// 运行测试环境中的工作流，该工作流以无法序列化的结果完成，并断言其任务
    /// 持久化地进入故障状态，同时 Grain 仍能提交后续工作。
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

        // 故障与其他结果一样得到提交，因此下一个激活实例无须再次运行工作流即可恢复该故障结果。
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

/// <summary>没有编解码器的类型的值，因此无法序列化。</summary>
internal sealed class NoCodecValue;

/// <summary>当设置 <see cref="PoisonedRequest.DecodeFails"/> 时无法解码的值，模拟类型被移除的情况。</summary>
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

/// <summary>仅在 <see cref="DecodeFails"/> 未设置时才能解码其参数的请求，其任务体等待 <see cref="Release"/>。</summary>
[GenerateSerializer]
[Alias("Microsoft.Orleans.DurableTasks.Tests.PoisonedRequest")]
internal sealed class PoisonedRequest : IDurableTaskRequest
{
    /// <summary>获取或设置解码请求时是否失败。仅 <see cref="DurableTaskStoredPayloadTests"/> 使用此属性。</summary>
    public static bool DecodeFails { get; set; }

    /// <summary>获取或设置请求任务体等待的同步关卡。</summary>
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
