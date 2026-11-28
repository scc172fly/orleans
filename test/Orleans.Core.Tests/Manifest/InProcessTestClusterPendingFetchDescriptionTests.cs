using System;
using System.Collections.Immutable;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Runtime.Metadata;
using Orleans.TestingHost;
using Xunit;

namespace UnitTests.Manifest;

/// <summary>
/// 直接测试 <see cref="InProcessTestCluster.DescribePendingFetch"/>，以解决 Copilot 审查
/// 在 dotnet/orleans#11438 中发现的覆盖缺口：没有测试覆盖此诊断字符串的确切格式
/// （成员资格版本、已发布版本、挂起的 Silo、已用时间及最近一次故障）。
/// </summary>
public class InProcessTestClusterPendingFetchDescriptionTests
{
    [Fact]
    public void DescribePendingFetch_WithNoRecordedAttempt_DescribesOnlyThePublishedVersion()
    {
        var observer = CreateSiloAddress(11111, 1);
        var manifest = new ClusterManifest(new MajorMinorVersion(3, 2), ImmutableDictionary<SiloAddress, GrainManifest>.Empty);

        var description = InProcessTestCluster.DescribePendingFetch(observer, manifest, diagnostics: null, observerUtcNow: DateTime.UtcNow);

        Assert.Equal($"{observer}: no manifest-update attempt recorded (published version {manifest.Version})", description);
    }

    [Fact]
    public void DescribePendingFetch_WithPendingSilosAndAFailure_IncludesVersionsPendingElapsedAndFailure()
    {
        var observer = CreateSiloAddress(11111, 1);
        var pendingPeer = CreateSiloAddress(11112, 1);
        var failedPeer = CreateSiloAddress(11113, 1);
        var manifest = new ClusterManifest(new MajorMinorVersion(5, 0), ImmutableDictionary<SiloAddress, GrainManifest>.Empty);
        var attemptStartedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var failureAt = attemptStartedAt.AddSeconds(2);
        var observerUtcNow = attemptStartedAt.AddSeconds(12.3);
        var diagnostics = new ManifestUpdateAttemptDiagnostics(
            AttemptId: 7,
            MembershipVersion: new MembershipVersion(5),
            AttemptStartedAt: attemptStartedAt,
            PendingSilos: ImmutableHashSet.Create(pendingPeer),
            LastFailedSilo: failedPeer,
            LastFailureMessage: "simulated fetch failure",
            LastFailureAt: failureAt);

        var description = InProcessTestCluster.DescribePendingFetch(observer, manifest, diagnostics, observerUtcNow);

        Assert.Equal(
            $"{observer}: membership v{diagnostics.MembershipVersion}, published v{manifest.Version}, "
            + $"pending=[{pendingPeer}], attempt started 12.3s ago"
            + $", last fetch failure for {failedPeer} at {failureAt:O}: {diagnostics.LastFailureMessage}",
            description);
    }

    private static SiloAddress CreateSiloAddress(int port, int generation) =>
        SiloAddress.New(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port), generation);
}
