using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Orleans.Runtime;
using Orleans.Runtime.GrainDirectory;
using Orleans.Runtime.Placement;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests
{
    /// <summary>
    /// 测试 grain 激活可从无效的 grain 目录条目中恢复。
    /// 
    /// grain 目录目录中可包含指向
    /// 不存在激活。 可能因为:
    /// - Silo 清理目录条目前撞车
    /// - 造成状态不一致的网络分割区
    /// - 启动/停止作用期间的种族条件
    /// 
    /// Orleans 必须具备适应这些假设情景的能力,具体做法是:
    /// - 在激活期间检测无效条目
    /// - 使用适当的 previousRegistration 参数重新进行注册
    /// - 根据目录条目向正确的 silo 转发呼叫
    /// 
    /// 这些测试核实 Orleans 可从毒物目录条目中回收
    /// 并保持单一引爆约束。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class GrainLocatorActivationResiliencyTests : HostedTestClusterEnsureDefaultStarted
    {
        public GrainLocatorActivationResiliencyTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 当 grain 目录包含无效条目指时测试恢复
        /// 至启动地点的 silo 。
        /// 
        /// 假设情景:
        /// 1. 插入一个“poison”目录条目,指在主 silo上不存在激活
        /// 2. 试图启动 grain 在同一 silo
        /// 3. 激活应检测无效条目(相同 silo,不同激活识别码)
        /// 4. 将毒物条目重新登记为 previousRegistration ,以更新该条目
        /// 
        /// 此测试本地 silo自我健康无效目录状态的能力 。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task ReactivateGrainWithPoisonGrainDirectoryEntry_LocalSilo()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var primarySilo = (InProcessSiloHandle)Fixture.HostedCluster.Primary!;
            var primarySiloAddress = primarySilo.SiloAddress;
            var grain = GrainFactory.GetGrain<IGuidTestGrain>(Guid.NewGuid());

            // 创建毒物目录条目 :
            // - 正确点 silo (主)
            // - 但是假的启动身份证明并不存在
            // 此模拟从崩溃/ 停止激活 grain中生成的删除条目
            var grainLocator = primarySilo.SiloHost.Services.GetRequiredService<GrainLocator>();
            var badAddress = GrainAddress.GetAddress(primarySiloAddress, grain.GetGrainId(), ActivationId.NewId());
            await grainLocator.Register(badAddress, previousRegistration: null, cancellationToken);

            {
                // 使用安插提示在主 silo 上强制放置
                // 尽管有毒物条目, grain 仍应成功激活
                RequestContext.Set(IPlacementDirector.PlacementHintKey, primarySiloAddress);
                var silo = await grain.GetSiloAddress();
                Assert.Equal(primarySiloAddress, silo);
            }
        }

        /// <summary>
        /// 当尝试在与毒物条目不同的 silo 上进行激活时,测试恢复。
        /// 
        /// 假设情景:
        /// 1. 插入指向初级 silo的毒物条目
        /// 2. 试图在二级启动 silo
        /// 3. 中级 silo 查看目录条目,并将电话转发给初级
        /// 4. 初级 silo 检测无效条目并在激活期间更新
        /// 
        /// 交易时的跨silo 协调和传递行为
        /// 含有无效的目录条目, 确保呼叫到达右 silo。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task ReactivateGrainWithPoisonGrainDirectoryEntry_RemoteSilo()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var primarySilo = (InProcessSiloHandle)Fixture.HostedCluster.Primary!;
            var secondarySiloAddress = Fixture.HostedCluster.SecondarySilos.First().SiloAddress;
            var primarySiloAddress = primarySilo.SiloAddress;
            var grain = GrainFactory.GetGrain<IGuidTestGrain>(Guid.NewGuid());

            // 创建指向初级 silo 的毒物条目,并使用假活化身份识别
            // 测试转发机制:二级 silo 将看到此条目
            // 并将请求转交将恢复工作地点的初级机构
            var grainLocator = primarySilo.SiloHost.Services.GetRequiredService<GrainLocator>();
            var badAddress = GrainAddress.GetAddress(primarySiloAddress, grain.GetGrainId(), ActivationId.NewId());
            await grainLocator.Register(badAddress, previousRegistration: null, cancellationToken);

            {
                // 尝试在中学 silo上放置位置,但目录条目
                // 将会导致向初级 silo转运。
                // 1. 中等教育确认由于现有条目,它不应启动
                // 2. 电话根据名录发送至主线
                // 3. 成功从毒物入和启动中初步恢复
                RequestContext.Set(IPlacementDirector.PlacementHintKey, secondarySiloAddress);
                var silo = await grain.GetSiloAddress();
                Assert.Equal(primarySiloAddress, silo);
            }
        }
    }
}
