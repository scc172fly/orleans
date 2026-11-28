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
            TestChurn(new int[] { 0 }, new int[] { });    // 开头有 1 个故障节点。
            TestChurn(new int[] { 0, 1 }, new int[] { }); // 开头有 2 个故障节点。

            TestChurn(new int[] { count - 1 }, new int[] { });            // 末尾有 1 个故障节点。
            TestChurn(new int[] { count - 1, count - 2 }, new int[] { }); // 末尾有 2 个故障节点。

            TestChurn(new int[] { count / 2 }, new int[] { });                // 中间有 1 个故障节点。
            TestChurn(new int[] { count / 2, 1 + count / 2 }, new int[] { }); // 中间有 2 个故障节点。

            TestChurn(new int[] { 1, count - 2 }, new int[] { }); // 相隔一定距离的 2 个故障节点。
            TestChurn(new int[] { 0, count - 1 }, new int[] { }); // 相隔一定距离的 2 个故障节点。
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring"), TestCategory("RingStandalone")]
        public void RingStandalone_Joins()
        {
            TestChurn(new int[] { }, new int[] { 0 });     // 开头有 1 个加入节点。
            TestChurn(new int[] { }, new int[] { 0, 1 });  // 开头有 2 个加入节点。

            TestChurn(new int[] { }, new int[] { count - 1 });             // 末尾有 1 个加入节点。
            TestChurn(new int[] { }, new int[] { count - 1, count - 2 });  // 末尾有 2 个加入节点。

            TestChurn(new int[] { }, new int[] { count / 2 });                 // 中间有 1 个加入节点。
            TestChurn(new int[] { }, new int[] { count / 2, 1 + count / 2 });  // 中间有 2 个加入节点。

            TestChurn(new int[] { }, new int[] { 1, count - 2 });  // 相隔一定距离的 2 个加入节点。
            TestChurn(new int[] { }, new int[] { 0, count - 1 });  // 相隔一定距离的 2 个加入节点。
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring"), TestCategory("RingStandalone")]
        public void RingStandalone_Mixed()
        {
            TestChurn(new int[] { 0 }, new int[] { 1 });     // 开头的节点依次为故障节点、加入节点（FJ）。
            TestChurn(new int[] { 1 }, new int[] { 0 });     // 开头的节点依次为加入节点、故障节点（JF）。

            TestChurn(new int[] { count - 2 }, new int[] { count - 1 });     // 末尾的节点依次为故障节点、加入节点（FJ）。
            TestChurn(new int[] { count - 1 }, new int[] { count - 2 });     // 末尾的节点依次为加入节点、故障节点（JF）。

            TestChurn(new int[] { count / 2 }, new int[] { 1 + count / 2 });     // 中间的节点依次为故障节点、加入节点（FJ）。
            TestChurn(new int[] { 1 + count / 2 }, new int[] { count / 2 });     // 中间的节点依次为加入节点、故障节点（JF）。

            TestChurn(new int[] { 0 }, new int[] { count - 1 });     // 开头故障（F），末尾加入（J）。
            TestChurn(new int[] { count - 1 }, new int[] { 0 });     // 末尾故障（F），开头加入（J）。

        }

        private void TestChurn(int[] indexesFails, int[] indexesJoins)
        {
            Dictionary<SiloAddress, ConsistentRingProvider> rings, holder = new Dictionary<SiloAddress, ConsistentRingProvider>();
            List<SiloAddress> sortedServers, fail = new List<SiloAddress>();

            rings = CreateServers(count);

            sortedServers = rings.Keys.ToList();
            sortedServers.Sort(CompareSiloAddressesByHash);
            // 故障
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
            RemoveServers(rings, fail);     // 使节点故障。
            rings = Combine(rings, holder); // 加入新节点。
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
            // 将 set2 中的每个节点告知 set1。
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

            // 将 set1 中的每个节点告知 set2……即使 set1 和 set2 重叠，ConsistentRingProvider 也应能处理。
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

            // 将 set2 中的每个节点告知 set2……注意，set1 已经知道 set1 中的节点。
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

            // 合并集合。
            return set1.Union(set2).ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        private void RemoveServers(Dictionary<SiloAddress, ConsistentRingProvider> rings, List<SiloAddress> remove)
        {
            foreach (SiloAddress addr in remove)
            {
                rings.Remove(addr);
            }

            // 将故障节点告知每个节点。
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
                // 检查两个节点的负责范围是否互不重叠。
                Assert.True(fullring.Remove(r.GetMyRange()), string.Format("Couldn't find & break range {0} in {1}. Some other node already claimed responsibility.", r.GetMyRange(), fullring));
            }
            Assert.True(fullring.NumRanges == 0, string.Format("Range not completely covered. Uncovered ranges: {0}", fullring));
        }

        /// <summary>
        /// 根据 SiloAddress 的一致性哈希码进行比较。
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
                    // 实际比较在此处进行。
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
                    if (m.Begin == m.End) // 将完整范围作为特殊情况处理。
                    {
                        found = true;
                        ranges.Remove(m);
                        if (s.Begin != s.End) // 若 s 也是完整范围，则无需继续……整个范围已被覆盖。
                        {
                            ranges.Add((RangeFactory.CreateRange(s.End, s.Begin) as SingleRange)!);
                        }
                        break;
                    }

                    if (m.InRange(s.Begin + 1) && m.InRange(s.End)) // s 不能与两个单一范围重叠。
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
