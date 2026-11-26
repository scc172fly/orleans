using Orleans.Runtime;
using Xunit;

namespace UnitTests.General;

/// <summary>
/// CounterAggregatorGroup测试,这是 Orleans仪器和测量基础设施的一部分。
/// 本构成部分汇总多个来源的对数值,在整个 Orleans 中用于收集
/// 信息计数、启动计数和其他业务统计等业绩计量。
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
    /// 证明聚合器组缓存集合器实例以避免生成重复
    /// 同一公号名称和标签组合。
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
    /// 测试从该组内所有计数器中检索总综合指标值的收集机制。
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
    /// 压力测试,以核实同时高负荷下反聚合物的线条安全。
    /// 这确保了在 Orleans多轨运行环境中收集指标的工作仍然准确。
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
