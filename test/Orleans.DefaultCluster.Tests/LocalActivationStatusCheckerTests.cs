using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests;

/// <summary>
/// ILocalActivationStatusChecker 功能测试。
/// 验证激活状态检查器能从 Silo 和客户端两个视角
/// 正确识别在本地激活的 Grain。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Runtime")]
public class LocalActivationStatusCheckerTests(DefaultClusterFixture fixture) : HostedTestClusterEnsureDefaultStarted(fixture)
{
    /// <summary>
    /// 测试已在本地 Silo 激活的 Grain 的 IsLocallyActivated 是否返回 true。
    /// 验证调用 Grain 方法（从而激活 Grain）后，Silo 端的
    /// ILocalActivationStatusChecker 能正确将该 Grain 识别为本地已激活。
    /// </summary>
    [Fact, TestCategory("BVT")]
    public async Task ShouldReturnTrueForLocallyActivatedGrain()
    {
        // 获取 Grain 引用，并通过调用方法激活它
        var grain = GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.NextInt64());
        await grain.SetA(42);

        // 该 Grain 应恰好在一个 Silo 上本地激活
        var grainId = grain.GetGrainId();
        Assert.Single(HostedCluster.Silos, silo => IsLocallyActivated(silo, grainId));

        static bool IsLocallyActivated(SiloHandle silo, GrainId grainId)
        {
            var siloHandle = (InProcessSiloHandle)silo;
            var checker = siloHandle.ServiceProvider.GetRequiredService<ILocalActivationStatusChecker>();
            return checker.IsLocallyActivated(grainId);
        }
    }

    /// <summary>
    /// 测试尚未激活的 Grain 的 IsLocallyActivated 是否返回 false。
    /// 验证已创建但从未调用过的 Grain 引用
    /// 不会被视为已在本地激活。
    /// </summary>
    [Fact, TestCategory("BVT")]
    public void ShouldReturnFalseForNonActivatedGrain()
    {
        // 获取 Grain 引用，但不激活它
        var grain = GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.NextInt64());

        // 该 Grain 在任何 Silo 上都不应被本地激活
        var grainId = grain.GetGrainId();
        Assert.DoesNotContain(HostedCluster.Silos, silo => IsLocallyActivated(silo, grainId));

        static bool IsLocallyActivated(SiloHandle silo, GrainId grainId)
        {
            var siloHandle = (InProcessSiloHandle)silo;
            var checker = siloHandle.ServiceProvider.GetRequiredService<ILocalActivationStatusChecker>();
            return checker.IsLocallyActivated(grainId);
        }
    }

    /// <summary>
    /// 测试不同 Grain 标识的 IsLocallyActivated 是否返回 false。
    /// 验证激活一个 Grain 不会影响另一个 Grain 的激活状态。
    /// </summary>
    [Fact, TestCategory("BVT")]
    public async Task ShouldReturnFalseForDifferentGrainIdentity()
    {
        // 激活一个 Grain
        var grain1 = GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.NextInt64());
        await grain1.SetA(42);

        // 获取不同的 Grain 引用（不同标识）
        var grain2 = GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.NextInt64());

        // 第二个 Grain 在任何 Silo 上都不应被本地激活
        var grainId2 = grain2.GetGrainId();
        Assert.DoesNotContain(HostedCluster.Silos, silo => IsLocallyActivated(silo, grainId2));

        static bool IsLocallyActivated(SiloHandle silo, GrainId grainId)
        {
            var siloHandle = (InProcessSiloHandle)silo;
            var checker = siloHandle.ServiceProvider.GetRequiredService<ILocalActivationStatusChecker>();
            return checker.IsLocallyActivated(grainId);
        }
    }

    /// <summary>
    /// 测试客户端的 ILocalActivationStatusChecker 是否始终返回 false。
    /// 客户端不托管 Grain 激活实例，因此 IsLocallyActivated 应始终返回 false，
    /// 无论该 Grain 是否曾被调用。
    /// </summary>
    [Fact, TestCategory("BVT")]
    public async Task ClientShouldAlwaysReturnFalseForIsLocallyActivated()
    {
        // 获取 Grain 引用并激活它
        var grain = GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.NextInt64());
        await grain.SetA(42);

        // 获取客户端激活状态检查器
        var clientChecker = Client.ServiceProvider.GetRequiredService<ILocalActivationStatusChecker>();
        var isLocalOnClient = clientChecker.IsLocallyActivated(grain.GetGrainId());

        // 客户端应始终返回 false
        Assert.False(isLocalOnClient, "Client should always return false for IsLocallyActivated");
    }

    /// <summary>
    /// 测试客户端检查器即使对于未激活的 Grain 也返回 false。
    /// 验证无论 Grain 激活状态如何，行为均一致。
    /// </summary>
    [Fact, TestCategory("BVT")]
    public void ClientShouldReturnFalseForNonActivatedGrain()
    {
        // 获取 Grain 引用而不激活它
        var grain = GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.NextInt64());

        // 获取客户端激活状态检查器
        var clientChecker = Client.ServiceProvider.GetRequiredService<ILocalActivationStatusChecker>();
        var isLocalOnClient = clientChecker.IsLocallyActivated(grain.GetGrainId());

        // 客户端应返回 false
        Assert.False(isLocalOnClient, "Client should return false for non-activated grain");
    }
}
