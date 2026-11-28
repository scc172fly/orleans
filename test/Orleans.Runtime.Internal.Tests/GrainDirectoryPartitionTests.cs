using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Runtime.GrainDirectory;
using Xunit;

namespace UnitTests;

/// <summary>
/// 测试 Orleans Grain 目录分区功能。
/// 
/// Grain 目录是一种分布式数据结构，将 Grain 标识映射
/// 至其当前激活位置（Silo 地址）。关键概念：
/// 
/// - 单一激活约束：每个 Grain ID 应最多有一个激活。
/// - 目录分区：为实现可伸缩性，目录在多个 Silo 上分区。
/// - Silo 死亡处理：必须清理已死亡 Silo 的条目，以允许新激活。
/// - 竞争条件处理：必须正确同步并发激活请求。
/// 
/// 这些测试验证关键场景：
/// - 覆盖指向已死亡 Silo 的条目。
/// - 防止存活 Silo 上出现重复激活。
/// - 使用 previousAddress 参数进行条件更新。
/// - 查找期间过滤掉指向已死亡 Silo 的条目。
/// 
/// 这对于维持 Orleans 的单一激活保证至关重要。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("GrainDirectory")]
[TestCategory("BVT"), TestCategory("GrainDirectory")]
public class GrainDirectoryPartitionTests
{
    private readonly LocalGrainDirectoryPartition _target;
    private readonly MockClusterMembershipService _clusterMembershipService;
    private static readonly SiloAddress LocalSiloAddress = SiloAddress.FromParsableString("127.0.0.1:11111@123");
    private static readonly SiloAddress OtherSiloAddress = SiloAddress.FromParsableString("127.0.0.2:11111@456");

    public GrainDirectoryPartitionTests()
    {
        _clusterMembershipService = new MockClusterMembershipService();
        _clusterMembershipService.SetSiloStatus(LocalSiloAddress, SiloStatus.Active);
        _target = new LocalGrainDirectoryPartition(
            _clusterMembershipService,
            Options.Create(new GrainDirectoryOptions()),
            new LoggerFactory());
    }

    /// <summary>
    /// 测试能否覆盖指向已死亡 Silo 的目录条目。
    /// Silo 死亡时，其 Grain 激活失效。目录
    /// 必须允许新激活替换这些失效条目，以保持
    /// 可用性。这对于 Silo 故障后的恢复至关重要。
    /// </summary>
    [Fact]
    public void OverrideDeadEntryTest()
    {
        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Active);

        var grainId = GrainId.Create("testGrain", "myKey");
        var firstGrainAddress = GrainAddress.NewActivationAddress(OtherSiloAddress, grainId);

        // 插入指向有效 Silo 的有效条目。
        var firstRegister = _target.AddSingleActivation(firstGrainAddress, previousAddress: null);
        Assert.Equal(firstGrainAddress, firstRegister.Address);

        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Dead);

        // 先前的条目现在指向已死亡的 Silo，应可以覆盖该条目。
        var secondGrainAddress = GrainAddress.NewActivationAddress(LocalSiloAddress, grainId);
        var secondRegister = _target.AddSingleActivation(secondGrainAddress, previousAddress: null);
        Assert.Equal(secondGrainAddress, secondRegister.Address);
    }

    /// <summary>
    /// 验证目录拒绝在已知死亡的 Silo 上
    /// 注册激活的尝试。这可防止创建
    /// 立即失效的条目，避免不必要的
    /// 清理工作和潜在竞争条件。
    /// </summary>
    [Fact]
    public void DoNotInsertInvalidEntryTest()
    {
        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Dead);

        var grainId = GrainId.Create("testGrain", "myKey");
        var grainAddress = GrainAddress.NewActivationAddress(OtherSiloAddress, grainId);

        // 插入指向已死亡 Silo 的无效条目。
        Assert.Throws<OrleansException>(() => _target.AddSingleActivation(grainAddress, previousAddress: null));
    }

    /// <summary>
    /// 通过确保有效条目不能被新激活尝试覆盖，
    /// 测试单一激活保证。这可防止多个 Silo 尝试
    /// 并发激活同一个 Grain 时产生
    /// 重复激活。
    /// </summary>
    [Fact]
    public void DoNotOverrideValidEntryTest()
    {
        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Active);

        var grainId = GrainId.Create("testGrain", "myKey");
        var grainAddress = GrainAddress.NewActivationAddress(OtherSiloAddress, grainId);

        // 插入指向有效 Silo 的有效条目。
        var register = _target.AddSingleActivation(grainAddress, previousAddress: null);
        Assert.Equal(grainAddress, register.Address);

        // 先前的条目仍有效，因此应无法覆盖。
        var newGrainAddress = GrainAddress.NewActivationAddress(LocalSiloAddress, grainId);
        var newRegister = _target.AddSingleActivation(newGrainAddress, previousAddress: null);
        Assert.Equal(grainAddress, newRegister.Address);
    }

    /// <summary>
    /// 测试使用 previousAddress 参数的条件更新功能。
    /// 这支持显式移交场景，即有意将激活
    /// 从一个 Silo 移到另一个 Silo，但仅在当前状态符合
    /// 预期时才进行（乐观并发控制）。
    /// </summary>
    [Fact]
    public void OverrideValidEntryIfMatchesTest()
    {
        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Active);

        var grainId = GrainId.Create("testGrain", "myKey");
        var grainAddress = GrainAddress.NewActivationAddress(OtherSiloAddress, grainId);

        // 插入指向有效 Silo 的有效条目。
        var register = _target.AddSingleActivation(grainAddress, previousAddress: null);
        Assert.Equal(grainAddress, register.Address);

        // 先前条目仍有效，但若将它作为 AddSingleActivation 调用的 "previousAddress" 参数传入，应可以覆盖该条目。
        var newGrainAddress = GrainAddress.NewActivationAddress(LocalSiloAddress, grainId);
        var newRegister = _target.AddSingleActivation(newGrainAddress, previousAddress: grainAddress);
        Assert.Equal(newGrainAddress, newRegister.Address);
    }

    /// <summary>
    /// 验证 previousAddress 与当前条目不匹配时，
    /// 条件更新失败。这可防止目录
    /// 状态在读取与更新操作之间发生变化所导致的竞争条件，确保分布式场景下
    /// 的一致性。
    /// </summary>
    [Fact]
    public void DoNotOverrideValidEntryIfNoMatchTest()
    {
        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Active);

        var grainId = GrainId.Create("testGrain", "myKey");
        var grainAddress = GrainAddress.NewActivationAddress(OtherSiloAddress, grainId);
        var nonMatchingAddress = new GrainAddress
        {
            GrainId = grainAddress.GrainId,
            ActivationId = ActivationId.NewId(),
            SiloAddress = OtherSiloAddress,
            MembershipVersion = new MembershipVersion(grainAddress.MembershipVersion.Value + 1),
        };

        // 插入指向有效 Silo 的有效条目。
        var register = _target.AddSingleActivation(grainAddress, previousAddress: null);
        Assert.Equal(grainAddress, register.Address);

        // 先前条目仍有效，而且我们提供了不匹配的 previousAddress，因此不应覆盖现有值。
        var newGrainAddress = GrainAddress.NewActivationAddress(LocalSiloAddress, grainId);
        var newRegister = _target.AddSingleActivation(newGrainAddress, previousAddress: nonMatchingAddress);
        Assert.Equal(grainAddress, newRegister.Address);
    }

    /// <summary>
    /// 测试查找能否过滤掉指向已死亡 Silo 的条目。
    /// 即使目录中包含过时条目，查找也应
    /// 返回 null，而非无效地址，以便调用方
    /// 在健康的 Silo 上创建新激活。
    /// </summary>
    [Fact]
    public void DoNotReturnInvalidEntryTest()
    {
        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Active);

        var grainId = GrainId.Create("testGrain", "myKey");
        var grainAddress1 = GrainAddress.NewActivationAddress(OtherSiloAddress, grainId);

        // 插入指向有效 Silo 的有效条目。
        var register1 = _target.AddSingleActivation(grainAddress1, previousAddress: null);
        Assert.Equal(grainAddress1, register1.Address);

        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Dead);

        // 先前条目已失效，不应返回。
        var lookup = _target.LookUpActivation(grainId);
        Assert.Null(lookup.Address);
    }

    [Theory]
    [InlineData(SiloStatus.ShuttingDown)]
    [InlineData(SiloStatus.Stopping)]
    public void ReturnEntryForTerminatingButNotDeadSilo(SiloStatus status)
    {
        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, status);

        var grainId = GrainId.Create("testGrain", "myKey");
        var grainAddress = GrainAddress.NewActivationAddress(OtherSiloAddress, grainId);

        var register = _target.AddSingleActivation(grainAddress, previousAddress: null);
        Assert.Equal(grainAddress, register.Address);

        var lookup = _target.LookUpActivation(grainId);
        Assert.Equal(grainAddress, lookup.Address);
    }

    private sealed class MockClusterMembershipService : IClusterMembershipService
    {
        private readonly Dictionary<SiloAddress, (SiloStatus Status, string Name)> _statuses = new();
        private long _version;

        public ClusterMembershipSnapshot CurrentSnapshot { get; private set; } =
            new(ImmutableDictionary<SiloAddress, ClusterMember>.Empty, MembershipVersion.MinValue);

        public IAsyncEnumerable<ClusterMembershipSnapshot> MembershipUpdates => throw new NotImplementedException();

        public void SetSiloStatus(SiloAddress siloAddress, SiloStatus status)
        {
            _statuses[siloAddress] = (status, "TestSilo");
            var members = ImmutableDictionary.CreateBuilder<SiloAddress, ClusterMember>();
            foreach (var (silo, entry) in _statuses)
            {
                members[silo] = new ClusterMember(silo, entry.Status, entry.Name);
            }

            CurrentSnapshot = new ClusterMembershipSnapshot(members.ToImmutable(), new MembershipVersion(++_version));
        }

        public ValueTask Refresh(MembershipVersion minimumVersion = default, CancellationToken cancellationToken = default) => default;

        public Task<bool> TryKill(SiloAddress siloAddress) => throw new NotImplementedException();
    }
}
