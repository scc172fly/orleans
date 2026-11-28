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
    /// 测试 Grain 激活能否从无效的 Grain 目录条目中恢复。
    /// 
    /// Grain 目录可能包含指向不存在激活的过时或“有害”条目。
    /// 这可能由以下原因导致：
    /// - Silo 在清理目录条目前崩溃。
    /// - 网络分区导致状态不一致。
    /// - 激活或停用期间发生竞争条件。
    /// 
    /// Orleans 必须通过以下方式抵御这些情况：
    /// - 在激活期间检测无效条目。
    /// - 使用适当的 previousRegistration 参数重试注册。
    /// - 根据目录条目将调用转发至正确的 Silo。
    /// 
    /// 这些测试验证 Orleans 能否从有害目录条目中恢复，
    /// 并维持单一激活约束。
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
        /// 测试 Grain 目录包含指向即将进行激活的同一 Silo 的无效条目时，
        /// 能否恢复。
        /// 
        /// 场景：
        /// 1. 插入一个指向主 Silo 上不存在的激活的“有害”目录条目。
        /// 2. 尝试在同一个 Silo 上激活 Grain。
        /// 3. 激活应检测到无效条目（同一 Silo，不同激活 ID）。
        /// 4. 将有害条目作为 previousRegistration 重试注册，以更新该条目。
        /// 
        /// 这测试本地 Silo 自行修复无效目录状态的能力。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task ReactivateGrainWithPoisonGrainDirectoryEntry_LocalSilo()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var primarySilo = (InProcessSiloHandle)Fixture.HostedCluster.Primary!;
            var primarySiloAddress = primarySilo.SiloAddress;
            var grain = GrainFactory.GetGrain<IGuidTestGrain>(Guid.NewGuid());

            // 创建有害目录条目：
            // - 指向正确的 Silo（主 Silo）。
            // - 但使用的是一个不存在的伪造激活实例 ID。
            // 这模拟崩溃或已停用 Grain 遗留的过时条目。
            var grainLocator = primarySilo.SiloHost.Services.GetRequiredService<GrainLocator>();
            var badAddress = GrainAddress.GetAddress(primarySiloAddress, grain.GetGrainId(), ActivationId.NewId());
            await grainLocator.Register(badAddress, previousRegistration: null, cancellationToken);

            {
                // 使用放置提示强制将 Grain 放置在主 Silo 上。
                // 尽管存在有害条目，Grain 仍应成功激活。
                RequestContext.Set(IPlacementDirector.PlacementHintKey, primarySiloAddress);
                var silo = await grain.GetSiloAddress();
                Assert.Equal(primarySiloAddress, silo);
            }
        }

        /// <summary>
        /// 测试在有害条目指向的 Silo 之外尝试激活时能否恢复。
        /// 
        /// 场景：
        /// 1. 插入指向主 Silo 的有害条目。
        /// 2. 尝试在次级 Silo 上激活。
        /// 3. 次级 Silo 看到目录条目，并将调用转发给主 Silo。
        /// 4. 主 Silo 检测到无效条目，并在激活期间更新它。
        /// 
        /// 这测试处理无效目录条目时的跨 Silo 协调与转发行为，
        /// 确保调用到达正确的 Silo。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task ReactivateGrainWithPoisonGrainDirectoryEntry_RemoteSilo()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var primarySilo = (InProcessSiloHandle)Fixture.HostedCluster.Primary!;
            var secondarySiloAddress = Fixture.HostedCluster.SecondarySilos.First().SiloAddress;
            var primarySiloAddress = primarySilo.SiloAddress;
            var grain = GrainFactory.GetGrain<IGuidTestGrain>(Guid.NewGuid());

            // 创建一条指向主 Silo 的错误目录条目，使用伪造的激活实例 ID。
            // 这测试转发机制：次级 Silo 会看到此条目，
            // 并将请求转发给主 Silo，由主 Silo 进行恢复。
            var grainLocator = primarySilo.SiloHost.Services.GetRequiredService<GrainLocator>();
            var badAddress = GrainAddress.GetAddress(primarySiloAddress, grain.GetGrainId(), ActivationId.NewId());
            await grainLocator.Register(badAddress, previousRegistration: null, cancellationToken);

            {
                // 尝试放置到次级 Silo 上，但目录条目
                // 会导致转发至主 Silo。这验证：
                // 1. 次级 Silo 根据现有条目判断不应执行激活。
                // 2. 根据目录将调用转发至主 Silo。
                // 3. 主 Silo 从有害条目恢复并成功激活。
                RequestContext.Set(IPlacementDirector.PlacementHintKey, secondarySiloAddress);
                var silo = await grain.GetSiloAddress();
                Assert.Equal(primarySiloAddress, silo);
            }
        }
    }
}
