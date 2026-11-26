using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests;

/// <summary>
/// ILocalActivationStatusChecker 功能测试。
/// 验证激活状态检查器正确识别本地启动 grains
/// 从 silo 和客户端角度出发。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Runtime")]
public class LocalActivationStatusCheckerTests(DefaultClusterFixture fixture) : HostedTestClusterEnsureDefaultStarted(fixture)
{
    /// <summary>
    /// 测试 IsLocallyActivated 返回 Rec Ret 的正数 grain 在当地 silo.
    /// 证明在调用 grain 方法(该方法激活了 grain)之后, silo- 侧
    /// ILocalActivationStatusChecker 正确标明 grain 是本地激活的 。
    /// </summary>
    [Fact, TestCategory("BVT")]
    public async Task ShouldReturnTrueForLocallyActivatedGrain()
    {
        // 获取 grain 引用,然后调用一种方法激活它
        var grain = GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.NextInt64());
        await grain.SetA(42);

        // grain 应在一个精确的 silo上就地激活
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
    /// 测试 IsLocallyActivated 返回没有被激活的 grain 时是否为虚假。
    /// 证明没有提及已经建立但从未援引的 grain
    /// 当地视为激活。
    /// </summary>
    [Fact, TestCategory("BVT")]
    public void ShouldReturnFalseForNonActivatedGrain()
    {
        // 获得 grain 引用, 但不要激活它
        var grain = GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.NextInt64());

        // grain 不应在任何 silo上就地激活
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
    /// 测试 IsLocallyActivated 返回不同 grain 身份的错误。
    /// 证明激活一个 grain 不影响另一个 grain的启动状态。
    /// </summary>
    [Fact, TestCategory("BVT")]
    public async Task ShouldReturnFalseForDifferentGrainIdentity()
    {
        // 激活一个 grain
        var grain1 = GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.NextInt64());
        await grain1.SetA(42);

        // 获取不同的 grain 引用(不同身份)
        var grain2 = GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.NextInt64());

        // 第二次 grain 不应在任何 silo
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
    /// 测试客户端 ILocalActivationStatusChecker 总是返回错误 。
    /// 客户端不主机 grain 激活, 因此 IsLocallyActivated 总是应该返回假
    /// 无论是否援引了 grain 。
    /// </summary>
    [Fact, TestCategory("BVT")]
    public async Task ClientShouldAlwaysReturnFalseForIsLocallyActivated()
    {
        // 获取 grain 引用并激活
        var grain = GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.NextInt64());
        await grain.SetA(42);

        // 获取客户端激活检查器
        var clientChecker = Client.ServiceProvider.GetRequiredService<ILocalActivationStatusChecker>();
        var isLocalOnClient = clientChecker.IsLocallyActivated(grain.GetGrainId());

        // 客户总是应该回假的
        Assert.False(isLocalOnClient, "Client should always return false for IsLocallyActivated");
    }

    /// <summary>
    /// 测试客户端检查器即使未激活也返回假 grains。
    /// 不论 grain 启动状态如何,都验证一致的行为。
    /// </summary>
    [Fact, TestCategory("BVT")]
    public void ClientShouldReturnFalseForNonActivatedGrain()
    {
        // 获取 grain 引用, 不激活它
        var grain = GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.NextInt64());

        // 获取客户端激活检查器
        var clientChecker = Client.ServiceProvider.GetRequiredService<ILocalActivationStatusChecker>();
        var isLocalOnClient = clientChecker.IsLocallyActivated(grain.GetGrainId());

        // 客户应假返回
        Assert.False(isLocalOnClient, "Client should return false for non-activated grain");
    }
}
