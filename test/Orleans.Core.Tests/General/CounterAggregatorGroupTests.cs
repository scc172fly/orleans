using Orleans.Runtime;
using Xunit;

namespace UnitTests.General;

/// <summary>
/// 测试 CounterAggregatorGroup，它是 Orleans 检测与指标基础设施的一部分。
/// 此组件聚合来自多个来源的计数器值，Orleans 广泛使用它来收集
/// 消息数、激活数及其他运行统计等性能指标。
/// </summary>
[TestSuite("Functional")]
[TestProvider("None")]
[TestArea("Runtime")]
public class CounterAggregatorGroupTests
{
    private readonly ITestOutputHelper _output;
    public CounterAggregatorGroupTests(ITestOutputHelper output)
    {
        this._output = output;
    }

    /// <summary>
    /// 验证聚合器组缓存聚合器实例，避免为相同指标名称和标签组合
    /// 创建重复实例。
    /// </summary>
    [Fact, TestCategory("Functional"), TestCategory("Aggregators")]
    public void ValidateAggregatorCache()
    {
        var group = new CounterAggregatorGroup();

        var aggregator1 = group.FindOrCreate(new("foo", "bar"));
        var aggregator2 = group.FindOrCreate(new("foo", "bar"));

        Assert.Same(aggregator1, aggregator2);
        Assert.Single(group.Aggregators);
    }

    /// <summary>
    /// 测试从组中所有计数器检索聚合指标值的收集机制。
    /// </summary>
    [Fact, TestCategory("Functional"), TestCategory("Aggregators")]
    public void Collect()
    {
        var group = new CounterAggregatorGroup();

        var aggregator1 = group.FindOrCreate(new("foo", "bar1"));
        var aggregator2 = group.FindOrCreate(new("foo", "bar2"));

        aggregator1.Add(1);
        aggregator1.Add(2);
        aggregator2.Add(2);
        aggregator2.Add(3);

        var measurements = group.Collect().ToList();
        Assert.Equal(2, measurements.Count);
        Assert.Equal(3, measurements.Single(m => m.Tags[0].Value is "bar1").Value);
        Assert.Equal(5, measurements.Single(m => m.Tags[0].Value is "bar2").Value);
    }

    /// <summary>
    /// 通过压力测试验证高并发负载下计数器聚合器的线程安全性。
    /// 这确保 Orleans 多线程运行时环境中的指标收集保持准确。
    /// </summary>
    [Fact, TestCategory("Functional"), TestCategory("Aggregators")]
    public void TestMultithreadedCorrectness()
    {
        int numOfIterations = 1000000;

        var group = new CounterAggregatorGroup();
        var counterCount = Environment.ProcessorCount;

        Parallel.For(0, Environment.ProcessorCount, j =>
        {
            for (int i = 0; i < counterCount; i++)
            {
                var aggregator = group.FindOrCreate(new("test", i));

                for (int k = 0; k < numOfIterations; k++)
                {
                    aggregator.Add(i);
                }
            }
        });

        var measurements = group.Collect().OrderBy(m => m.Tags[0].Value).ToList();
        foreach (var measurement in measurements)
        {
            var i = (int)measurement.Tags[0].Value!;
            _output.WriteLine("{0} {1}", i, measurement.Value);
            Assert.Equal(i * Environment.ProcessorCount * numOfIterations, measurement.Value);
        }
    }

}
