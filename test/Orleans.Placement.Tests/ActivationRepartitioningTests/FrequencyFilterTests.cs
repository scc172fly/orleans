using System.Text;
using Orleans.Runtime.Placement.Repartitioning;
using Xunit;

namespace UnitTests.ActivationRepartitioningTests;

public class FrequencyFilterTests
{
    [Fact]
    public void GetExpectedTopK()
    {
        const int NumSamples = 10_000;
        var sink = new UlongFrequentItemCollection(100);
        var random = new Random();
        var distribution = new ZipfRejectionSampler(random, 1000, 0.5);
        for (var i = 0; i < NumSamples; i++)
        {
            var sample = (ulong)distribution.Sample();
            sink.Add(new TestKey(sample));

            if (i == 4 * NumSamples / 5)
            {
                sink.Remove(new TestKey(3));
            }
        }

        var allCounters = sink.Elements.ToList();
        allCounters.Sort((left, right) => right.Count.CompareTo(left.Count));
        var sb = new StringBuilder();
        foreach (var (key, count, error) in allCounters)
        {
            if (error == 0)
            {
                sb.AppendLine($"{key.Key,3}: {count}");
            }
            else
            {
                sb.AppendLine($"{key.Key,3}: {count} ε{error}");
            }
        }

        var result = sb.ToString();
        Assert.NotEmpty(result);
    }

    public readonly struct TestKey(ulong key)
    {
        private static ulong _nextKey;
        public static TestKey GetNext() => new(_nextKey++);
        public readonly ulong Key = key;

        public override string ToString() => $"[{Key}]";
    }

    private sealed class UlongFrequentItemCollection(int capacity) : FrequentItemCollection<ulong, TestKey>(capacity)
    {
        protected override ulong GetKey(in TestKey element) => element.Key;
        public void Remove(in TestKey element) => RemoveCore(GetKey(element));
    }

    /// <summary>
    /// 生成近似 Zipf 分布。先前方法比 MathNet.Numerics 快 20 倍，但每秒只能生成 250 个样本。
    /// 此近似方法每秒可以生成超过 1,000,000 个样本。
    /// </summary>
    public class FastZipf
    {
        private static readonly Random SeededPrng = new(42);

        /// <summary>
        /// 生成 Zipf 分布。
        /// </summary>
        /// <param name="random">要使用的随机数生成器。</param>
        /// <param name="sampleCount">样本数量。</param>
        /// <param name="skew">偏斜度。s=0 时为均匀分布。随着 s 增大，排名靠前的项相较于排名靠后的稀有项，其出现概率迅速增大。</param>
        /// <param name="cardinality">N：基数，即项的总数。</param>
        /// <returns>Zipf 分布。</returns>
        public static long[] Generate(Random random, int sampleCount, double skew, int cardinality)
        {
            var sampler = new ZipfRejectionSampler(random, cardinality, skew);

            var samples = new long[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                samples[i] = sampler.Sample();
            }

            return samples;
        }

        /// <summary>
        /// 生成 Zipf 分布。
        /// </summary>
        /// <param name="sampleCount">样本数量。</param>
        /// <param name="skew">偏斜度。s=0 时为均匀分布。随着 s 增大，排名靠前的项相较于排名靠后的稀有项，其出现概率迅速增大。</param>
        /// <param name="cardinality">N：基数，即项的总数。</param>
        /// <returns>Zipf 分布。</returns>
        public static long[] Generate(int sampleCount, double skew, int cardinality) => Generate(SeededPrng, sampleCount, skew, cardinality);
    }

    // https://jasoncrease.medium.com/rejection-sampling-the-zipf-distribution-6b359792cffa
    public class ZipfRejectionSampler
    {
        private readonly Random _rand;
        private readonly double _skew;
        private readonly double _t;

        public ZipfRejectionSampler(Random random, long cardinality, double skew)
        {
            _rand = random;
            _skew = skew;
            _t = (Math.Pow(cardinality, 1 - skew) - skew) / (1 - skew);
        }

        public long Sample()
        {
            while (true)
            {
                var invB = bInvCdf(_rand.NextDouble());
                var sampleX = (long)(invB + 1);
                var yRand = _rand.NextDouble();
                var ratioTop = Math.Pow(sampleX, -_skew);
                var ratioBottom = sampleX <= 1 ? 1 / _t : Math.Pow(invB, -_skew) / _t;
                var rat = ratioTop / (ratioBottom * _t);

                if (yRand < rat)
                {
                    return sampleX;
                }
            }
        }
        private double bInvCdf(double p)
        {
            return p * _t switch
            {
                <= 1 => p * _t,
                _ => Math.Pow(p * _t * (1 - _skew) + _skew, 1 / (1 - _skew))
            };
        }
    }
}
