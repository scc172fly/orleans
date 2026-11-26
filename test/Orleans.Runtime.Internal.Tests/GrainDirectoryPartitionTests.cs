using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Runtime.GrainDirectory;
using Xunit;

namespace UnitTests;

/// <summary>
/// Orleans grain 目录分割区功能测试。
/// 
/// grain 目录是一个分布式数据结构,用于绘制 grain 身份
/// 至其当前启动地点(silo 关键概念:
/// 
/// - 单一引爆限制:每个 grain ID至多应有一个激活
/// -目录分区:目录在 silos 之间分隔,以便进行缩放
/// - Silo 死亡处理:必须清理已死亡的 silo 条目,以便允许新的激活
/// - 种族状况处理:同时启动请求必须适当同步
/// 
/// 这些测试验证了关键情景:
/// - 指死记数的压倒性条目 silos
/// - 防止在现场重复启动 silos
/// - 使用 previousAddress 参数进行有条件更新
/// - 在调查中过滤已死条目 silos
/// 
/// 这对于保持 Orleans单项启动保证至关重要。
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
    /// 测试指向死 silos 的目录条目可能被覆盖。
    /// 当 silo 死亡时,其 grain 激活失效。目录
    /// 需要允许新激活来替换这些已关闭的条目以维持
    /// 。这对于在 silo 故障后恢复至关重要。
    /// </summary>
    [Fact]
    public void OverrideDeadEntryTest()
    {
        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Active);

        var grainId = GrainId.Create("testGrain", "myKey");
        var firstGrainAddress = GrainAddress.NewActivationAddress(OtherSiloAddress, grainId);

        // 插入有效条目,指 silo
        var firstRegister = _target.AddSingleActivation(firstGrainAddress, previousAddress: null);
        Assert.Equal(firstGrainAddress, firstRegister.Address);

        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Dead);

        // 上一条条目指已死亡 silo, 现在应该有可能推翻它
        var secondGrainAddress = GrainAddress.NewActivationAddress(LocalSiloAddress, grainId);
        var secondRegister = _target.AddSingleActivation(secondGrainAddress, previousAddress: null);
        Assert.Equal(secondGrainAddress, secondRegister.Address);
    }

    /// <summary>
    /// 证明目录拒绝登记启动的尝试
    /// 日期 时 silos 已经已知已死亡的 。 这样可以防止创建
    /// 立即无效的条目, 避免不必要的
    /// 清洁工作和潜在的种族条件。
    /// </summary>
    [Fact]
    public void DoNotInsertInvalidEntryTest()
    {
        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Dead);

        var grainId = GrainId.Create("testGrain", "myKey");
        var grainAddress = GrainAddress.NewActivationAddress(OtherSiloAddress, grainId);

        // 插入无效条目,指已死 silo
        Assert.Throws<OrleansException>(() => _target.AddSingleActivation(grainAddress, previousAddress: null));
    }

    /// <summary>
    /// 通过确保有效输入测试单一激活保证
    /// 无法被新的激活尝试所推翻。 这样可以防止它被新启动尝试所覆盖 。
    /// 当多个 silos 试图激活
    /// 相同 grain 同时。
    /// </summary>
    [Fact]
    public void DoNotOverrideValidEntryTest()
    {
        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Active);

        var grainId = GrainId.Create("testGrain", "myKey");
        var grainAddress = GrainAddress.NewActivationAddress(OtherSiloAddress, grainId);

        // 插入有效条目,指 silo
        var register = _target.AddSingleActivation(grainAddress, previousAddress: null);
        Assert.Equal(grainAddress, register.Address);

        // 上一个条目仍然有效, 不应该能够推翻它
        var newGrainAddress = GrainAddress.NewActivationAddress(LocalSiloAddress, grainId);
        var newRegister = _target.AddSingleActivation(newGrainAddress, previousAddress: null);
        Assert.Equal(grainAddress, newRegister.Address);
    }

    /// <summary>
    /// 使用 previousAddress 参数测试有条件更新功能。
    /// 这样可以出现明确的结果情形,即启动是故意引爆
    /// 从 1 移动 silo 向另一个状态,但只有在当前状态符合
    /// 期望值(乐观的同汇货币控制)。
    /// </summary>
    [Fact]
    public void OverrideValidEntryIfMatchesTest()
    {
        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Active);

        var grainId = GrainId.Create("testGrain", "myKey");
        var grainAddress = GrainAddress.NewActivationAddress(OtherSiloAddress, grainId);

        // 插入有效条目,指 silo
        var register = _target.AddSingleActivation(grainAddress, previousAddress: null);
        Assert.Equal(grainAddress, register.Address);

        // 上一个条目仍然有效,但应该有可能推翻它,我们作为 " 提供该条目。previousAddress" 至 AddSingleActivation 电话
        var newGrainAddress = GrainAddress.NewActivationAddress(LocalSiloAddress, grainId);
        var newRegister = _target.AddSingleActivation(newGrainAddress, previousAddress: grainAddress);
        Assert.Equal(newGrainAddress, newRegister.Address);
    }

    /// <summary>
    /// 证明有条件更新在 previousAddress 不时失败
    /// 匹配当前条目的当前条目。这将防止目录所在的种族条件
    /// 读取和更新操作之间更改状态,确保一致性
    /// 分布式假设情景。
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

        // 插入有效条目,指 silo
        var register = _target.AddSingleActivation(grainAddress, previousAddress: null);
        Assert.Equal(grainAddress, register.Address);

        // 上一个条目仍然有效,我们提供了非匹配 previousAddress,因此现有值不应被推翻。
        var newGrainAddress = GrainAddress.NewActivationAddress(LocalSiloAddress, grainId);
        var newRegister = _target.AddSingleActivation(newGrainAddress, previousAddress: nonMatchingAddress);
        Assert.Equal(grainAddress, newRegister.Address);
    }

    /// <summary>
    /// 测试,查找过滤显示死亡的条目 silos。
    /// 即使目录含有已删除的条目, 搜索也应该
    /// 返回无效的地址而非无效地址,允许调用者
    /// 创建健康 silo上的新激活。
    /// </summary>
    [Fact]
    public void DoNotReturnInvalidEntryTest()
    {
        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Active);

        var grainId = GrainId.Create("testGrain", "myKey");
        var grainAddress1 = GrainAddress.NewActivationAddress(OtherSiloAddress, grainId);

        // 插入有效条目,指 silo
        var register1 = _target.AddSingleActivation(grainAddress1, previousAddress: null);
        Assert.Equal(grainAddress1, register1.Address);

        _clusterMembershipService.SetSiloStatus(OtherSiloAddress, SiloStatus.Dead);

        // 上一个条目不再有效, 不应该返回
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
