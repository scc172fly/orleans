using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Orleans.Configuration;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Runtime.Metadata;
using Xunit;

namespace UnitTests.Manifest;

/// <summary>
/// 运行橡胶棒/联合审查对点网/orleans#11438的确定结果:直接显示提取
/// 其尝试完成时仍在运行(e.g.),因为同行修理填补了每一个其他缺失的 silo
/// ) 没有取消, 所以可以在新的尝试已经开始后完成。 在修正之前,
/// <see cref="ClusterManifestProvider"/>的完成处理程序人无条件适用了这种完成,
/// 腐蚀了新尝试的诊断 与国家属于被取代的状态。
/// </summary>
public partial class ClusterManifestProviderTests
{
    [Fact]
    public async Task MarkManifestFetchComplete_IgnoresCompletion_FromSupersededAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var localSilo = CreateSiloAddress(11111, 1);
        var peer = CreateSiloAddress(11112, 1);

        // 第一次尝试: 没有丢失 silos, 所以它标记并立即完成对第一次尝试的诊断。
        var emptySnapshot = CreateActiveMembershipSnapshot(1, localSilo, []);
        using var membership = new TestClusterMembershipService(emptySnapshot);
        var grainFactory = Substitute.For<IInternalGrainFactory>();
        var options = new ClusterManifestOptions { EnableContentAddressedRetrieval = false };
        await using var provider = CreateClusterManifestProvider(localSilo, membership, grainFactory, options);
        await InitializeProviderAsync(provider, cancellationToken);
        Assert.True(await UpdateManifestAsync(provider, emptySnapshot, cancellationToken));

        var firstAttemptId = provider.LastAttemptDiagnostics!.AttemptId;

        // 第二次尝试:现在缺少一个对等点, 其提取从来无法单靠自己完成( 模拟直接
        // 端端端口修理后左接左接接左接接, 提供每个缺失 silo 用于 *下个* 尝试,所以 UpdateManifest
        // 运行时不等待完成,它将仍然未定。
        var secondSnapshot = CreateActiveMembershipSnapshot(2, localSilo, [peer]);
        var pendingFetch = new TaskCompletionSource<GrainManifest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = Substitute.For<ISiloManifestSystemTarget>();
        target.GetSiloManifest(Arg.Any<CancellationToken>()).Returns(_ => new ValueTask<GrainManifest>(pendingFetch.Task));
        grainFactory.GetSystemTarget<ISiloManifestSystemTarget>(Constants.ManifestProviderType, peer).Returns(target);
        var secondUpdate = UpdateManifestAsync(provider, secondSnapshot, cancellationToken);

        var secondAttempt = provider.LastAttemptDiagnostics;
        Assert.NotNull(secondAttempt);
        Assert.NotEqual(firstAttemptId, secondAttempt!.AttemptId);
        Assert.Contains(peer, secondAttempt.PendingSilos);
        Assert.Null(secondAttempt.LastFailedSilo);

        // 带有第一次(现为后被)尝试的 ID 的完成不能变换第二次尝试的
        // 尽管其目标相同 silo ,但第二次尝试确实在等待。
        var staleFailure = new InvalidOperationException("stale fetch failure from a superseded attempt");
        MarkManifestFetchComplete(provider, firstAttemptId, peer, staleFailure);

        var afterStaleCompletion = provider.LastAttemptDiagnostics;
        Assert.NotNull(afterStaleCompletion);
        Assert.Equal(secondAttempt.AttemptId, afterStaleCompletion!.AttemptId);
        Assert.Contains(peer, afterStaleCompletion.PendingSilos);
        Assert.Null(afterStaleCompletion.LastFailedSilo);
        Assert.Null(afterStaleCompletion.LastFailureMessage);

        // 实际完成时,如带有当前尝试的ID,通常适用。
        pendingFetch.SetResult(CreateGrainManifest());
        Assert.True(await secondUpdate);
        var final = provider.LastAttemptDiagnostics;
        Assert.NotNull(final);
        Assert.Equal(secondAttempt.AttemptId, final!.AttemptId);
        Assert.DoesNotContain(peer, final.PendingSilos);
        Assert.Null(final.LastFailedSilo);
    }
}
