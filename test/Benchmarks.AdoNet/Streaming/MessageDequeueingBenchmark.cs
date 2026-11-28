using Microsoft.Data.SqlClient;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Orleans.Streaming.AdoNet;
using Orleans.Tests.SqlUtils;
using UnitTests.General;
using static System.String;

namespace Benchmarks.AdoNet.Streaming;

public class SqlServerMessageDequeuingBenchmark() : MessageDequeuingBenchmark(AdoNetInvariants.InvariantNameSqlServer, "OrleansStreamTest")
{
    public override void GlobalSetup()
    {
        base.GlobalSetup();

        SqlConnection.ClearAllPools();
    }
}

/// <summary>
/// 本基准测试衡量消息队列操作的性能。
/// </summary>
[WarmupCount(1), IterationCount(3), InvocationCount(1), MarkdownExporter]
public abstract class MessageDequeuingBenchmark(string invariant, string database)
{
    private const int OperationsPerInvoke = 1000;

    private readonly Consumer _consumer = new();
    // BenchmarkDotNet 在读取这些字段之前调用 GlobalSetup。
    private IRelationalStorage _storage = default!;
    private RelationalOrleansQueries _queries = default!;
    private byte[] _payload = [];
    private string[] _queueIds = default!;
    private AdoNetStreamMessageAck[] _acks = [];

    /// <summary>
    /// 此项用于体现队列并发导致的性能下降。
    /// </summary>
    [Params(1, 4, 8)]
    public int QueueCount { get; set; }

    /// <summary>
    /// 此项用于体现消息有效载荷大小导致的性能下降。
    /// </summary>
    [Params(10000)]
    public int PayloadSize { get; set; }

    /// <summary>
    /// 此项用于体现批次大小不同时的性能变化。
    /// </summary>
    [Params(1, 16, 32)]
    public int BatchSize { get; set; }

    /// <summary>
    /// 此项用于体现数据表填充程度不同所产生的性能变化。
    /// </summary>
    [Params(0, 0.5, 1)]
    public double FullnessRatio { get; set; }

    [GlobalSetup]
    public virtual void GlobalSetup()
    {
        Async().GetAwaiter().GetResult();

        async Task Async()
        {
            // 创建大小合适的有效载荷
            _payload = new byte[PayloadSize];
            Array.Fill<byte>(_payload, 0xFF);

            // 定义队列集合
            _queueIds = Enumerable.Range(0, QueueCount).Select(i => $"QueueId-{i}").ToArray();

            // 初始化测试数据库
            var testing = await RelationalStorageForTesting.SetupInstance(invariant, database);
            if (IsNullOrEmpty(testing.CurrentConnectionString))
            {
                throw new InvalidOperationException($"Database '{database}' not initialized");
            }
            _storage = RelationalStorage.CreateInstance(invariant, testing.CurrentConnectionString);
            _queries = await RelationalOrleansQueries.CreateInstance(invariant, testing.CurrentConnectionString);
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        Async().GetAwaiter().GetResult();

        async Task Async()
        {
            await _storage.ExecuteAsync("TRUNCATE TABLE OrleansStreamMessage");

            // 生成用于出队的测试数据
            var count = (int)Math.Ceiling(OperationsPerInvoke * QueueCount * BatchSize * FullnessRatio);
            _acks = new AdoNetStreamMessageAck[count];
            await Parallel.ForAsync(0, count, async (i, ct) =>
            {
                // 按队列轮询顺序生成消息，以模拟多个代理的行为
                var queueId = _queueIds[i % _queueIds.Length];
                var ack = await _queries.QueueStreamMessageAsync("ServiceId-0", "ProviderId-0", queueId, _payload, 1000);

                _acks[i] = ack;
            });
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public async Task GetStreamMessages()
    {
        var count = OperationsPerInvoke * QueueCount;

        await Parallel.ForAsync(0, count, new ParallelOptions { MaxDegreeOfParallelism = QueueCount }, async (i, ct) =>
        {
            // 按轮询顺序获取队列 ID，以模拟多个代理的行为
            var queueId = _queueIds[i % _queueIds.Length];

            // 获取对应确认操作的队列中的消息
            // 根据数据填充程度和批次大小参数，队列中可能有也可能没有可出队的数据
            // 无论是否有数据，都执行出队操作以测量其开销
            var messages = await _queries.GetStreamMessagesAsync("ServiceId-0", "ProviderId-0", queueId, BatchSize, 1, 1000, 1000, 1000, 1000);

            _consumer.Consume(messages);
        });
    }
}
