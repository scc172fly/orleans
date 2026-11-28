using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTDefaultCluster.Tests.General
{
    /// <summary>
    /// Orleans 请求上下文功能测试。
    /// 请求上下文使环境数据（例如关联 ID、用户上下文
    /// 或请求元数据）能够随 Grain 调用传递，而无需显式作为
    /// 参数传递。它类似 Orleans 中的线程本地存储或异步本地存储，
    /// 但专为跨 Grain 和 Silo 的分布式调用设计。
    /// </summary>
    [TestArea("Runtime")]
    public class RequestContextTests : HostedTestClusterEnsureDefaultStarted
    {
        public RequestContextTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 测试请求上下文从调用方传递到被调用方。
        /// 验证 Grain 调用之前在 RequestContext 中设置的数据
        /// 在 Grain 方法执行期间可用，展示
        /// 跨 Grain 边界自动传播环境上下文的机制。
        /// 这适用于关联 ID、身份验证令牌等。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("RequestContext")]
        public async Task RequestContextCallerToCalleeFlow()
        {
            var grain = this.GrainFactory.GetGrain<ISimplePersistentGrain>(Random.Shared.Next());
            // 设置要发送给 Grain 的上下文
            RequestContext.Set("GrainInfo", 10);
            // 此 Grain 方法读取上下文并返回它。
            var infoFromGrain = await grain.GetRequestContext();
            Assert.NotNull(infoFromGrain);
            Assert.True((int)infoFromGrain == 10);

            Assert.Contains("GrainInfo", RequestContext.Keys);
        }

        /// <summary>
        /// 测试请求上下文从被调用方传回调用方。
        /// 此测试本应验证在 Grain 方法中设置到 RequestContext 的数据，
        /// 在调用完成后可供调用方使用。
        /// 注意：当前跳过此测试，因为该功能可能尚未完全受支持。
        /// 为保持隔离，请求上下文通常单向传递（从调用方到被调用方）。
        /// </summary>
        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact(Skip = "Was failing before (just masked as a Pass), needs fixing or removing"), TestCategory("RequestContext"), TestCategory("Functional")]
        public async Task RequestContextCalleeToCallerFlow()
        {
            var grain = this.GrainFactory.GetGrain<ISimplePersistentGrain>(Random.Shared.Next());
            // Grain 中的此方法会调用 RequestContext.Set
            await grain.SetRequestContext(15);
            // 读取 Grain 中设置的信息
            var infoFromGrain = RequestContext.Get("GrainInfo");
            Assert.NotNull(infoFromGrain);
            Assert.True((int)infoFromGrain == 15);

            Assert.Contains("GrainInfo", RequestContext.Keys);
        }
    }
}
