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
/// 测试针对 dotnet/orleans#11438 上橡皮鸭式审查/Copilot 审查所发现问题的修复：如果一次尝试结束时，某个直接清单获取操作
/// 仍在运行（例如，对等修复先填补了所有其他缺失的 Silo），
/// 它不会被取消，因此可能在较新的尝试已开始后才完成。修复之前，
/// <see cref="ClusterManifestProvider"/> 的完成处理程序会无条件应用此类完成结果，
/// 从而以已被取代的尝试的状态破坏较新尝试的诊断信息。
/// </summary>
public partial class ClusterManifestProviderTests
{
    [Fact]
    public async Task MarkManifestFetchComplete_IgnoresCompletion_FromSupersededAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var localSilo = CreateSiloAddress(11111, 1);
        var peer = CreateSiloAddress(11112, 1);

        // 第一次尝试：没有缺失的 Silo，因此记录并立即结束尝试 1 的诊断。
        var emptySnapshot = CreateActiveMembershipSnapshot(1, localSilo, []);
        using var membership = new TestClusterMembershipService(emptySnapshot);
        var grainFactory = Substitute.For<IInternalGrainFactory>();
        var options = new ClusterManifestOptions { EnableContentAddressedRetrieval = false };
        await using var provider = CreateClusterManifestProvider(localSilo, membership, grainFactory, options);
        await InitializeProviderAsync(provider, cancellationToken);
        Assert.True(await UpdateManifestAsync(provider, emptySnapshot, cancellationToken));

        var firstAttemptId = provider.LastAttemptDiagnostics!.AttemptId;

        // 第二次尝试：现在缺失一个对等节点，且其获取操作永远不会自行完成（模拟对等修复已为
        // *本次*尝试提供每个缺失 Silo 的清单后仍在运行的直接获取操作，因此 UpdateManifest
        // 从不等待它）。运行时不等待其完成，它会保持挂起。
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

        // 携带第一次（现已被取代的）尝试 ID 的完成操作不得修改第二次尝试的
        // 诊断信息，即使它的目标是第二次尝试确实正在等待的同一个 Silo。
        var staleFailure = new InvalidOperationException("stale fetch failure from a superseded attempt");
        MarkManifestFetchComplete(provider, firstAttemptId, peer, staleFailure);

        var afterStaleCompletion = provider.LastAttemptDiagnostics;
        Assert.NotNull(afterStaleCompletion);
        Assert.Equal(secondAttempt.AttemptId, afterStaleCompletion!.AttemptId);
        Assert.Contains(peer, afterStaleCompletion.PendingSilos);
        Assert.Null(afterStaleCompletion.LastFailedSilo);
        Assert.Null(afterStaleCompletion.LastFailureMessage);

        // 携带当前尝试 ID 的实际完成操作按正常方式应用。
        pendingFetch.SetResult(CreateGrainManifest());
        Assert.True(await secondUpdate);
        var final = provider.LastAttemptDiagnostics;
        Assert.NotNull(final);
        Assert.Equal(secondAttempt.AttemptId, final!.AttemptId);
        Assert.DoesNotContain(peer, final.PendingSilos);
        Assert.Null(final.LastFailedSilo);
    }
}
