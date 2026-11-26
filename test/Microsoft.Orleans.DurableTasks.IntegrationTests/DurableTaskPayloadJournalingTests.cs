using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Journaling;
using Xunit;
using Orleans.Durable.Tasks.Runtime;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 证明耐久 grain RPC日记每份 grain日记中都要求并产生一次有效载荷。
/// </summary>
/// <remarks>
/// 被调用者收发箱接受直接援引任务状态,其发件箱的日记只提到
/// 由存储结果产生。
/// 任务状态将有效载荷与元数据分开, 因此元数据更新永远不会重写
/// 。 JSON 格式将字节数组写成基数64字符串,因此,这些计数也搜索解码字符串。
/// </remarks>
public abstract partial class DurableTaskPayloadJournalingTests(string journalFormatKey) : IAsyncLifetime
{
    private readonly DurableTaskRecoveryFixture _fixture = new(journalFormatKey);

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task RemoteCallJournalsEachPayloadOncePerGrainAndDeletesDeliveredResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var caller = _fixture.Client.GetGrain<IPayloadCallerGrain>(Guid.NewGuid());
        var worker = _fixture.Client.GetGrain<IPayloadWorkerGrain>(Guid.NewGuid());
        var seed = Random.Shared.Next();
        var input = PayloadMarkers.Input(seed);
        var output = PayloadMarkers.Transform(input);
        using var events = DurableTaskEventBarriers.Collect();

        var scheduled = await caller.CallAsync(worker.GetPrimaryKey(), seed).WithId("payload-call").ScheduleAsync(cancellationToken);
        var response = await scheduled.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.True(response.GetResult<bool>());

        // 呼叫者收听的邮箱接受了完成, 所以被呼叫者的发信箱就把它退了,删除任务。
        var childId = await events.WaitForCallerChildAsync(worker.GetGrainId(), caller.GetGrainId(), TaskId.CreateRoot("payload-call").Child(PayloadCallerGrain.CallId));
        await events.WaitForDeletedAsync(worker.GetGrainId(), childId);

        var callerJournal = await ReadJournalAsync(caller.GetGrainId());
        var workerJournal = await ReadJournalAsync(worker.GetGrainId());
        Assert.Equal(1, CountPayload(callerJournal, input));
        Assert.Equal(1, CountPayload(workerJournal, input));
        Assert.Equal(1, CountPayload(workerJournal, output));
        Assert.Equal(1, CountPayload(callerJournal, output));
        Assert.DoesNotContain("complete-ack", Encoding.Latin1.GetString(callerJournal), StringComparison.Ordinal);
        Assert.DoesNotContain("complete-ack", Encoding.Latin1.GetString(workerJournal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientCallJournalsEachPayloadOnceAndRetainsResultForPolling()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var worker = _fixture.Client.GetGrain<IPayloadWorkerGrain>(Guid.NewGuid());
        var input = PayloadMarkers.Input(Random.Shared.Next());
        var output = PayloadMarkers.Transform(input);

        var scheduled = await worker.TransformAsync(input).WithId("client-call").ScheduleAsync(cancellationToken);
        var response = await scheduled.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal(output, response.GetResult<byte[]>());

        // 客户没有收件箱,所以结果留待投票后再写。
        var other = await worker.TransformAsync(PayloadMarkers.Input(Random.Shared.Next())).WithId("other-call").ScheduleAsync(cancellationToken);
        await other.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        var polled = await worker.GetDurableTask<byte[]>("client-call").GetResponseAsync(cancellationToken);
        Assert.Equal(output, polled.GetResult<byte[]>());

        var workerJournal = await ReadJournalAsync(worker.GetGrainId());
        Assert.Equal(1, CountPayload(workerJournal, input));
        Assert.Equal(1, CountPayload(workerJournal, output));
    }

    private async Task<byte[]> ReadJournalAsync(GrainId grainId)
    {
        var consumer = new JournalBytes();
        await _fixture.Storage.CreateStorage(JournalId.FromGrainId(grainId)).ReadAsync(consumer, TestContext.Current.CancellationToken);
        return consumer.ToArray();
    }

    private static int CountPayload(byte[] journal, byte[] payload)
    {
        var count = CountOccurrences(journal, payload);
        foreach (Match match in Base64String().Matches(Encoding.Latin1.GetString(journal)))
        {
            var buffer = new byte[match.Groups[1].Length];
            if (Convert.TryFromBase64String(match.Groups[1].Value, buffer, out var written))
            {
                count += CountOccurrences(buffer.AsSpan(0, written), payload);
            }
        }

        return count;
    }

    private static int CountOccurrences(ReadOnlySpan<byte> data, ReadOnlySpan<byte> value)
    {
        var count = 0;
        for (var index = data.IndexOf(value); index >= 0; index = data.IndexOf(value))
        {
            count++;
            data = data[(index + value.Length)..];
        }

        return count;
    }

    [GeneratedRegex("\"([A-Za-z0-9+/]{24,}={0,2})\"")]
    private static partial Regex Base64String();

    private sealed class JournalBytes : IJournalStorageConsumer
    {
        private readonly MemoryStream _bytes = new();

        public void Read(JournalBufferReader buffer, IJournalMetadata? metadata)
        {
            while (buffer.Length > 0)
            {
                var chunk = new byte[buffer.Length];
                buffer.Read(chunk);
                _bytes.Write(chunk);
            }
        }

        public byte[] ToArray() => _bytes.ToArray();
    }
}

/// <summary>运行有效载荷日志覆盖,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskPayloadJournalingTests() : DurableTaskPayloadJournalingTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行有效载荷日志覆盖,任务状态以 Orleans 二进制日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskPayloadJournalingTests() : DurableTaskPayloadJournalingTests(DurableTaskRecoveryFixture.OrleansBinary);

/// <summary>衍生的、可复制的有效载荷, 所以测试可以在日记字节中找到每个有效载荷。</summary>
public static class PayloadMarkers
{
    public static byte[] Input(int seed) => SHA512.HashData(BitConverter.GetBytes(seed));

    public static byte[] Transform(byte[] input) => SHA512.HashData(input);
}

public interface IPayloadCallerGrain : IGrainWithGuidKey
{
    DurableTask<bool> CallAsync(Guid workerKey, int seed);
}

public interface IPayloadWorkerGrain : IGrainWithGuidKey
{
    DurableTask<byte[]> TransformAsync(byte[] input);
}

[GrainType("durable-task-payload-caller-test")]
public sealed class PayloadCallerGrain : DurableGrain, IPayloadCallerGrain
{
    public const string CallId = "transform";

    // 调用器生成请求的有效有效载荷, 仅返回结果是否匹配, 所以只返回其根任务
    // 储存的载荷都不是有效载荷。
    public async DurableTask<bool> CallAsync(Guid workerKey, int seed)
    {
        var input = PayloadMarkers.Input(seed);
        var result = await GrainFactory.GetGrain<IPayloadWorkerGrain>(workerKey).TransformAsync(input).WithId(CallId);
        return result.AsSpan().SequenceEqual(PayloadMarkers.Transform(input));
    }
}

[GrainType("durable-task-payload-worker-test")]
public sealed class PayloadWorkerGrain : DurableGrain, IPayloadWorkerGrain
{
    public DurableTask<byte[]> TransformAsync(byte[] input) => DurableTask.FromResult(PayloadMarkers.Transform(input));
}
