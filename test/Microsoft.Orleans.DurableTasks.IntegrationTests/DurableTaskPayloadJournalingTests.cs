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
/// 验证持久化 Grain RPC 在每个 Grain 的日志中只记录一次每个请求与结果的有效载荷。
/// </summary>
/// <remarks>
/// 被调用方收件箱直接将调用接纳到任务状态中，其发件箱仅记录完成消息的引用，
/// 并从已存储结果构造完成消息。调用方收件箱直接将完成消息接纳到
/// 调用方的任务状态中。任务状态将有效载荷与元数据分开持久化，因此更新元数据绝不会重新写入
/// 有效载荷。JSON 格式将字节数组写成 base64 字符串，因此统计时也搜索解码后的字符串。
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

        // 调用方收件箱已接受完成消息，因此被调用方发件箱将其退役，并删除任务。
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

        // 客户端没有收件箱，因此进一步写入后，结果仍被保留以供轮询。
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

/// <summary>使用默认 JSON 日志格式存储任务状态，运行有效载荷日志记录相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskPayloadJournalingTests() : DurableTaskPayloadJournalingTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行有效载荷日志记录相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskPayloadJournalingTests() : DurableTaskPayloadJournalingTests(DurableTaskRecoveryFixture.OrleansBinary);

/// <summary>派生具有区分度且可复现的有效载荷，以便测试能在日志字节中找到每个有效载荷。</summary>
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

    // 调用方派生请求有效载荷，并且仅返回结果是否匹配，因此其自身根任务
    // 不存储这两个有效载荷中的任何一个。
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
