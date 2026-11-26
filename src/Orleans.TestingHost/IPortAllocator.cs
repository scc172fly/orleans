using System;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 寻找未使用的港口的功能。
    /// </summary>
    public interface ITestClusterPortAllocator : IDisposable
    {
        /// <summary>
        /// 分出连续的对端端端口。
        /// </summary>
        /// <param name="numPorts">连续指定港口的数量。</param>
        /// <returns>基地港口 silo 和网关端点。</returns>
        ValueTuple<int, int> AllocateConsecutivePortPairs(int numPorts);
    }
}
