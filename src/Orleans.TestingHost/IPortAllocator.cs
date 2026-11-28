using System;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 用于查找未使用端口的功能。
    /// </summary>
    public interface ITestClusterPortAllocator : IDisposable
    {
        /// <summary>
        /// 分配连续的端口对。
        /// </summary>
        /// <param name="numPorts">要分配的连续端口数量。</param>
        /// <returns>Silo 和网关端点的起始端口。</returns>
        ValueTuple<int, int> AllocateConsecutivePortPairs(int numPorts);
    }
}
