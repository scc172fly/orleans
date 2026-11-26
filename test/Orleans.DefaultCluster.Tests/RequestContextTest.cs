using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTDefaultCluster.Tests.General
{
    /// <summary>
    /// 请求上下文功能的 Orleans 测试。
    /// 请求环境允许流动的环境数据(如相关身份、用户背景、用户背景、
    /// 请求或请求提供元数据)以及 grain 调用电话,但没有明确通过
    /// 。这是 Orleans' 等量的线-本地存储或同步-本地
    /// 存储,但设计用于在 grains 和 silos之间分配调用。
    /// </summary>
    [TestArea("Runtime")]
    public class RequestContextTests : HostedTestClusterEnsureDefaultStarted
    {
        public RequestContextTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 测试请求由调用者到被调用者之间产生的背景。
        /// 证明该数据集 RequestContext 在 a 之前 grain 调
        /// grain 方法执行范围内可用,证明
        /// 环境环境环境的自动传播跨越 grain 边界。
        /// 这对相关身份识别、认证符号等有用。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("RequestContext")]
        public async Task RequestContextCallerToCalleeFlow()
        {
            var grain = this.GrainFactory.GetGrain<ISimplePersistentGrain>(Random.Shared.Next());
            // 设置要发送到 grain的上下文
            RequestContext.Set("GrainInfo", 10);
            // 此 grain 方法读取上下文并返回它
            var infoFromGrain = await grain.GetRequestContext();
            Assert.NotNull(infoFromGrain);
            Assert.True((int)infoFromGrain == 10);

            Assert.Contains("GrainInfo", RequestContext.Keys);
        }

        /// <summary>
        /// 测试请求由被调用者返回到调用者处的上下文。
        /// 将核实该数据集 RequestContext a 内 grain 方法
        /// 电话在通话结束后可供打电话者使用。
        /// 注:由于特性可能不完全支持,因此目前跳过这一测试。
        /// 请求通常单向流动(呼叫被传唤者),以孤立。
        /// </summary>
        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact(Skip = "Was failing before (just masked as a Pass), needs fixing or removing"), TestCategory("RequestContext"), TestCategory("Functional")]
        public async Task RequestContextCalleeToCallerFlow()
        {
            var grain = this.GrainFactory.GetGrain<ISimplePersistentGrain>(Random.Shared.Next());
            // grain 中的这一方法确实 RequestContext.Set
            await grain.SetRequestContext(15);
            // 读取 grain中的信息集
            var infoFromGrain = RequestContext.Get("GrainInfo");
            Assert.NotNull(infoFromGrain);
            Assert.True((int)infoFromGrain == 15);

            Assert.Contains("GrainInfo", RequestContext.Keys);
        }
    }
}
