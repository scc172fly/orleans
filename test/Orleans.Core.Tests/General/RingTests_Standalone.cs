using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Runtime;
using Orleans.Runtime.ConsistentRing;
using TestExtensions;
using Xunit;

namespace UnitTests.General
{
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class RingTests_Standalone
    {
        private const int count = 5;

        [Fact, TestCategory("Functional"), TestCategory("Ring"), TestCategory("RingStandalone")]
        public void RingStandalone_Basic()
        {
            Dictionary<SiloAddress, ConsistentRingProvider> rings = CreateServers(count);
            rings = Combine(rings, rings);
            VerifyRing(rings);
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring"), TestCategory("RingStandalone")]
        public void RingStandalone_Failures()
        {
            TestChurn(new int[] { 0 }, new int[] { });    // 开始 1 失败
            TestChurn(new int[] { 0, 1 }, new int[] { }); // 开始有2次失败

            TestChurn(new int[] { count - 1 }, new int[] { });            // 结尾处有1个失败
            TestChurn(new int[] { count - 1, count - 2 }, new int[] { }); // 结尾时有2次失败

            TestChurn(new int[] { count / 2 }, new int[] { });                // 中间有一个失败
            TestChurn(new int[] { count / 2, 1 + count / 2 }, new int[] { }); // 中间有2个失败

            TestChurn(new int[] { 1, count - 2 }, new int[] { }); // 距离2次故障
            TestChurn(new int[] { 0, count - 1 }, new int[] { }); // 距离2次故障
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring"), TestCategory("RingStandalone")]
        public void RingStandalone_Joins()
        {
            TestChurn(new int[] { }, new int[] { 0 });     // 1 加入开始
            TestChurn(new int[] { }, new int[] { 0, 1 });  // 2 加入起始部分

            TestChurn(new int[] { }, new int[] { count - 1 });             // 1 加入结尾处
            TestChurn(new int[] { }, new int[] { count - 1, count - 2 });  // 2 个加入到结尾处

            TestChurn(new int[] { }, new int[] { count / 2 });                 // 1人加入中间
            TestChurn(new int[] { }, new int[] { count / 2, 1 + count / 2 });  // 中间两队

            TestChurn(new int[] { }, new int[] { 1, count - 2 });  // 2个在一定距离接连
            TestChurn(new int[] { }, new int[] { 0, count - 1 });  // 2个在一定距离接连
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring"), TestCategory("RingStandalone")]
        public void RingStandalone_Mixed()
        {
            TestChurn(new int[] { 0 }, new int[] { 1 });     // 一开始是FJ
            TestChurn(new int[] { 1 }, new int[] { 0 });     // 一开始是JF

            TestChurn(new int[] { count - 2 }, new int[] { count - 1 });     // 末端是FJ
            TestChurn(new int[] { count - 1 }, new int[] { count - 2 });     // 最终的JF

            TestChurn(new int[] { count / 2 }, new int[] { 1 + count / 2 });     // 中间是FJ
            TestChurn(new int[] { 1 + count / 2 }, new int[] { count / 2 });     // 中间的JF

            TestChurn(new int[] { 0 }, new int[] { count - 1 });     // F第一,J最后
            TestChurn(new int[] { count - 1 }, new int[] { 0 });     // 最后一个是F,开头是J

        }

        private void TestChurn(int[] indexesFails, int[] indexesJoins)
        {
            Dictionary<SiloAddress, ConsistentRingProvider> rings, holder = new Dictionary<SiloAddress, ConsistentRingProvider>();
            List<SiloAddress> sortedServers, fail = new List<SiloAddress>();

            rings = CreateServers(count);

            sortedServers = rings.Keys.ToList();
            sortedServers.Sort(CompareSiloAddressesByHash);
            // 失败失败
            foreach (int i in indexesFails)
            {
                fail.Add(sortedServers[i]);
            }
            // 加入
            foreach (int i in indexesJoins)
            {
                holder.Add(sortedServers[i], rings[sortedServers[i]]);
                rings.Remove(sortedServers[i]);
            }
            rings = Combine(rings, rings);
            RemoveServers(rings, fail);     // 失败结点
            rings = Combine(rings, holder); // 加入新节点
            VerifyRing(rings);
        }

        private static Dictionary<SiloAddress, ConsistentRingProvider> CreateServers(int n)
        {
            Dictionary<SiloAddress, ConsistentRingProvider> rings = new Dictionary<SiloAddress, ConsistentRingProvider>();

            for (int i = 1; i <= n; i++)
            {
                SiloAddress addr = SiloAddressUtils.NewLocalSiloAddress(i);
                rings.Add(addr, new ConsistentRingProvider(addr, NullLoggerFactory.Instance, new FakeSiloStatusOracle()));
            }
            return rings;
        }

        private static Dictionary<SiloAddress, ConsistentRingProvider> Combine(Dictionary<SiloAddress, ConsistentRingProvider> set1, Dictionary<SiloAddress, ConsistentRingProvider> set2)
        {
            // 显示所有节点的设置1
            foreach (ConsistentRingProvider crp in set1.Values)
            {
                foreach (ConsistentRingProvider other in set2.Values)
                {
                    if (!crp.MyAddress.Equals(other.MyAddress))
                    {
                        other.AddServer(crp.MyAddress);
                    }
                }
            }

            // 设置1中每个节点的设置2 告诉 设置1 中的每一个节点 。 即使设置1 和 设置2 重叠, ConsistentRingProvider 也应该能够处理
            foreach (ConsistentRingProvider crp in set2.Values)
            {
                foreach (ConsistentRingProvider other in set1.Values)
                {
                    if (!crp.MyAddress.Equals(other.MyAddress))
                    {
                        other.AddServer(crp.MyAddress);
                    }
                }
            }

            // 设置2 显示所有节点的 设置2... 注意, 设置1 已经知道 设置 1 中 的节点
            foreach (ConsistentRingProvider crp in set2.Values)
            {
                foreach (ConsistentRingProvider other in set2.Values)
                {
                    if (!crp.MyAddress.Equals(other.MyAddress))
                    {
                        other.AddServer(crp.MyAddress);
                    }
                }
            }

            // 合并各组
            return set1.Union(set2).ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        private void RemoveServers(Dictionary<SiloAddress, ConsistentRingProvider> rings, List<SiloAddress> remove)
        {
            foreach (SiloAddress addr in remove)
            {
                rings.Remove(addr);
            }

            // 告诉每个节点失败的节点
            foreach (ConsistentRingProvider crp in rings.Values)
            {
                foreach (SiloAddress addr in remove)
                {
                    crp.RemoveServer(addr);
                }
            }
        }

        private void VerifyRing(Dictionary<SiloAddress, ConsistentRingProvider> rings)
        {
            RangeBreakable fullring = new RangeBreakable();

            foreach (ConsistentRingProvider r in rings.Values)
            {
                // 查看两个节点的责任之间是否有重叠
                Assert.True(fullring.Remove(r.GetMyRange()), string.Format("Couldn't find & break range {0} in {1}. Some other node already claimed responsibility.", r.GetMyRange(), fullring));
            }
            Assert.True(fullring.NumRanges == 0, string.Format("Range not completely covered. Uncovered ranges: {0}", fullring));
        }

        /// <summary>
        /// 比较 SiloAddress-基于其一致的散列代码
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        private static int CompareSiloAddressesByHash(SiloAddress x, SiloAddress y)
        {
            if (x == null)
            {
                return y == null ? 0 : -1;
            }
            else
            {
                if (y == null)
                {
                    return 1;
                }
                else
                {
                    // 实际比较在此
                    return x.GetConsistentHashCode().CompareTo(y.GetConsistentHashCode());
                }
            }
        }
    }

    internal sealed class FakeSiloStatusOracle : ISiloStatusOracle
    {
        private readonly Dictionary<SiloAddress, SiloStatus> _content = [];
        private readonly HashSet<ISiloStatusListener> _subscribers = [];

        public FakeSiloStatusOracle()
        {
            SiloAddress = SiloAddress.New(IPAddress.Loopback, Random.Shared.Next(2000, 40_000), SiloAddress.AllocateNewGeneration());
            _content[SiloAddress] = SiloStatus.Active;
        }

        public SiloStatus CurrentStatus => SiloStatus.Active;

        public string SiloName => "TestSilo";

        public SiloAddress SiloAddress { get; }

        public SiloStatus GetApproximateSiloStatus(SiloAddress siloAddress)
        {
            if (_content.TryGetValue(siloAddress, out var status))
            {
                return status;
            }
            return SiloStatus.None;
        }

        public Dictionary<SiloAddress, SiloStatus> GetApproximateSiloStatuses(bool onlyActive = false)
        {
            return onlyActive
                ? new Dictionary<SiloAddress, SiloStatus>(_content.Where(kvp => kvp.Value == SiloStatus.Active))
                : new Dictionary<SiloAddress, SiloStatus>(_content);
        }

        public void SetSiloStatus(SiloAddress siloAddress, SiloStatus status)
        {
            _content[siloAddress] = status;
            foreach (var subscriber in _subscribers)
            {
                subscriber.SiloStatusChangeNotification(siloAddress, status);
            }
        }

        public bool IsDeadSilo(SiloAddress silo) => GetApproximateSiloStatus(silo) == SiloStatus.Dead;

        public bool IsFunctionalDirectory(SiloAddress siloAddress) => !GetApproximateSiloStatus(siloAddress).IsTerminating();

        public bool SubscribeToSiloStatusEvents(ISiloStatusListener observer) => _subscribers.Add(observer);

        public bool TryGetSiloName(SiloAddress siloAddress, out string siloName)
        {
            siloName = "TestSilo";
            return true;
        }

        public bool UnSubscribeFromSiloStatusEvents(ISiloStatusListener observer) => _subscribers.Remove(observer);
        public SiloAddress[] GetActiveSilos() => [.. GetApproximateSiloStatuses(onlyActive: true).Keys];
    }

    internal class RangeBreakable
    {
        private List<SingleRange> ranges { get; set; }
        internal int NumRanges { get { return ranges.Count; } }

        public RangeBreakable()
        {
            ranges = new List<SingleRange>(1);
            ranges.Add((RangeFactory.CreateFullRange() as SingleRange)!);
        }

        public bool Remove(IRingRange range)
        {
            bool wholerange = true;
            foreach (var s in RangeFactory.GetSubRanges(range))
            {
                bool found = false;
                foreach (var m in ranges)
                {
                    if (m.Begin == m.End) // 将全带作为特例处理
                    {
                        found = true;
                        ranges.Remove(m);
                        if (s.Begin != s.End) // 如果 s 也完全范围, 那么故事的结尾... 覆盖整个范围
                        {
                            ranges.Add((RangeFactory.CreateRange(s.End, s.Begin) as SingleRange)!);
                        }
                        break;
                    }

                    if (m.InRange(s.Begin + 1) && m.InRange(s.End)) // 平方公尺
                    {
                        found = true;
                        ranges.Remove(m);
                        if (s.Begin != m.Begin)
                        {
                            ranges.Add((RangeFactory.CreateRange(m.Begin, s.Begin) as SingleRange)!);
                        }
                        if (s.End != m.End)
                        {
                            ranges.Add((RangeFactory.CreateRange(s.End, m.End) as SingleRange)!);
                        }
                        break;
                    }
                }
                wholerange = wholerange && found;
            }
            return wholerange;
        }
    }
}
