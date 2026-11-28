using Orleans.Runtime;
using Orleans.Runtime.TestHooks;

namespace Orleans.TestingHost
{
    /// <summary>
    /// <see cref="IClusterClient"/> 的扩展方法。
    /// </summary>
    internal static class ClientExtensions
    {
        /// <summary>
        /// 返回指定 Silo 的测试挂钩。
        /// </summary>
        /// <param name="client">客户端。</param>
        /// <param name="silo">Silo。</param>
        /// <returns>指定 Silo 的测试挂钩。</returns>
        public static ITestHooks GetTestHooks(this IClusterClient client, SiloHandle silo)
        {
            // 此处使用 siloAddress，而不是网关地址，因为目标 Silo 的网关可能
            // 并未与当前客户端连接。
            var internalClient = (IInternalClusterClient)client;
            return internalClient.GetSystemTarget<ITestHooksSystemTarget>(Constants.TestHooksSystemTargetType, silo.SiloAddress);
        }
    }
}
