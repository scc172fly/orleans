using Orleans.Runtime;
using Orleans.Runtime.TestHooks;

namespace Orleans.TestingHost
{
    /// <summary>
    /// <see cref="IClusterClient"/>的推广方法。
    /// </summary>
    internal static class ClientExtensions
    {
        /// <summary>
        /// 返回指定 silo的回溯测试钩。
        /// </summary>
        /// <param name="client">客户。</param>
        /// <param name="silo">下 silo.</param>
        /// <returns>指定 silo的测试钩。</returns>
        public static ITestHooks GetTestHooks(this IClusterClient client, SiloHandle silo)
        {
            // 使用此处的 siloAddress ,而不是网关地址,因为我们可能针对的是我们没有选择的 silo 
            // 连接到网关
            var internalClient = (IInternalClusterClient)client;
            return internalClient.GetSystemTarget<ITestHooksSystemTarget>(Constants.TestHooksSystemTargetType, silo.SiloAddress);
        }
    }
}
