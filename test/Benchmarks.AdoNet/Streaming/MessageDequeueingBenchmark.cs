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
/// 本基准衡量信息排队的绩效。
/// </summary>
[WarmupCount(1), IterationCount(3), InvocationCount(1), MarkdownExporter]
public abstract class MessageDequeuingBenchmark(string invariant, string database)
{
    private const int OperationsPerInvoke = 1000;

    private readonly Consumer _consumer = new();
    // 在阅读上述任何字段之前,BenchmarkDotNet 先援引 GlobalSetup 。
    private IRelationalStorage _storage = default!;
    private RelationalOrleansQueries _queries = default!;
    private byte[] _payload = [];
    private string[] _queueIds = default!;
    private AdoNetStreamMessageAck[] _acks = [];

    /// <summary>
    /// 这突显出队列同货币的退化。
    /// </summary>
    [Params(1, 4, 8)]
    public int QueueCount { get; set; }

    /// <summary>
    /// 这突出表明有效载荷大小的降解。
    /// </summary>
    [Params(10000)]
    public int PayloadSize { get; set; }

    /// <summary>
    /// 这突出显示按批量大小分列的差异。
    /// </summary>
    [Params(1, 16, 32)]
    public int BatchSize { get; set; }

    /// <summary>
    /// 这突出显示了表格的整数差异。
    /// </summary>
    [Params(0, 0.5, 1)]
    public double FullnessRatio { get; set; }

    [GlobalSetup]
    public virtual void GlobalSetup()
    {
        Async().GetAwaiter().GetResult();

        async Task Async()
        {
            // 创建适当大小的有效有效载荷
            _payload = new byte[PayloadSize];
            Array.Fill<byte>(_payload, 0xFF);

            // 定义设置的队列
            _queueIds = Enumerable.Range(0, QueueCount).Select(i => $"QueueId-{i}").ToArray();

            // 设置测试数据库
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

            // 生成要解排的测试数据
            var count = (int)Math.Ceiling(OperationsPerInvoke * QueueCount * BatchSize * FullnessRatio);
            _acks = new AdoNetStreamMessageAck[count];
            await Parallel.ForAsync(0, count, async (i, ct) =>
            {
                // 以圆轮 Robin 队列顺序生成消息,以帮助模拟多个代理器
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
            // 获得在轮 Robin 中队列代号,以便帮助模拟多个代理器
            var queueId = _queueIds[i % _queueIds.Length];

            // 获取 Ack 队列的信息
            // 和批量大小参数
            // 不论为了测量间接费用,我们不计其税
            var messages = await _queries.GetStreamMessagesAsync("ServiceId-0", "ProviderId-0", queueId, BatchSize, 1, 1000, 1000, 1000, 1000);

            _consumer.Consume(messages);
        });
    }
}
