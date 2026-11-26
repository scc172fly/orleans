#nullable enable
using Microsoft.Extensions.Logging;
using Orleans.GrainDirectory;
using TestExtensions;
using Xunit;

namespace Tester.Directories;

// 自定义 Grain 目录的基准测试
public abstract class GrainDirectoryTests<TGrainDirectory> where TGrainDirectory : IGrainDirectory
{
    protected readonly ILoggerFactory loggerFactory;
    private TGrainDirectory? _directory;

    protected GrainDirectoryTests(ITestOutputHelper testOutput)
    {
        this.loggerFactory = new LoggerFactory();
        this.loggerFactory.AddProvider(new XunitLoggerProvider(testOutput));
    }

    protected TGrainDirectory GrainDirectory => _directory ??= CreateGrainDirectory();

    protected abstract TGrainDirectory CreateGrainDirectory();

    [Fact]
    public async Task RegisterLookupUnregisterLookup()
    {
        var expected = new GrainAddress
        {
            ActivationId = ActivationId.NewId(),
            GrainId = GrainId.Parse("user/somerandomuser_" + Guid.NewGuid().ToString("N")),
            SiloAddress = SiloAddress.FromParsableString("10.0.23.12:1000@5678"),
            MembershipVersion = new MembershipVersion(51)
        };

        Assert.Equal(expected, await GrainDirectory.Register(expected, null, TestContext.Current.CancellationToken));

        Assert.Equal(expected, await GrainDirectory.Lookup(expected.GrainId, TestContext.Current.CancellationToken));

        await GrainDirectory.Unregister(expected, TestContext.Current.CancellationToken);

        Assert.Null(await GrainDirectory.Lookup(expected.GrainId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DoNotOverwriteEntry()
    {
        var expected = new GrainAddress
        {
            ActivationId = ActivationId.NewId(),
            GrainId = GrainId.Parse("user/somerandomuser_" + Guid.NewGuid().ToString("N")),
            SiloAddress = SiloAddress.FromParsableString("10.0.23.12:1000@5678"),
            MembershipVersion = new MembershipVersion(51)
        };

        var differentActivation = new GrainAddress
        {
            ActivationId = ActivationId.NewId(),
            GrainId = expected.GrainId,
            SiloAddress = SiloAddress.FromParsableString("10.0.23.12:1000@5678"),
            MembershipVersion = new MembershipVersion(51)
        };

        var differentSilo = new GrainAddress
        {
            ActivationId = expected.ActivationId,
            GrainId = expected.GrainId,
            SiloAddress = SiloAddress.FromParsableString("10.0.23.14:1000@4583"),
            MembershipVersion = new MembershipVersion(51)
        };

        Assert.Equal(expected, await GrainDirectory.Register(expected, null, TestContext.Current.CancellationToken));
        Assert.Equal(expected, await GrainDirectory.Register(differentActivation, null, TestContext.Current.CancellationToken));
        Assert.Equal(expected, await GrainDirectory.Register(differentSilo, null, TestContext.Current.CancellationToken));

        Assert.Equal(expected, await GrainDirectory.Lookup(expected.GrainId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// 如果注册电话包含匹配的 "previousAddress" 参数,则覆盖已有条目。
    /// </summary>
    [Fact]
    public async Task OverwriteEntryIfMatch()
    {
        var initial = new GrainAddress
        {
            ActivationId = ActivationId.NewId(),
            GrainId = GrainId.Parse("user/somerandomuser_" + Guid.NewGuid().ToString("N")),
            SiloAddress = SiloAddress.FromParsableString("10.0.23.12:1000@5678"),
            MembershipVersion = new MembershipVersion(51)
        };

        var differentActivation = new GrainAddress
        {
            ActivationId = ActivationId.NewId(),
            GrainId = initial.GrainId,
            SiloAddress = initial.SiloAddress,
            MembershipVersion = initial.MembershipVersion
        };

        var differentSilo = new GrainAddress
        {
            ActivationId = initial.ActivationId,
            GrainId = initial.GrainId,
            SiloAddress = SiloAddress.FromParsableString("10.0.23.14:1000@4583"),
            MembershipVersion = initial.MembershipVersion
        };

        // 成功,没有登记,所以忽略了上一个地址。
        Assert.Equal(initial, await GrainDirectory.Register(initial, differentSilo, TestContext.Current.CancellationToken));

        // 成功, 前一个地址与现有的登记相符 。
        Assert.Equal(differentActivation, await GrainDirectory.Register(differentActivation, initial, TestContext.Current.CancellationToken));

        // 失败时, 前一个地址与现有的登记不符 。
        Assert.Equal(differentActivation, await GrainDirectory.Register(differentSilo, initial, TestContext.Current.CancellationToken));

        Assert.Equal(differentActivation, await GrainDirectory.Lookup(initial.GrainId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DoNotDeleteDifferentActivationIdEntry()
    {
        var expected = new GrainAddress
        {
            ActivationId = ActivationId.NewId(),
            GrainId = GrainId.Parse("user/somerandomuser_" + Guid.NewGuid().ToString("N")),
            SiloAddress = SiloAddress.FromParsableString("10.0.23.12:1000@5678"),
            MembershipVersion = new MembershipVersion(51)
        };

        var otherEntry = new GrainAddress
        {
            ActivationId = ActivationId.NewId(),
            GrainId = expected.GrainId,
            SiloAddress = SiloAddress.FromParsableString("10.0.23.12:1000@5678"),
            MembershipVersion = new MembershipVersion(51)
        };

        Assert.Equal(expected, await GrainDirectory.Register(expected, null, TestContext.Current.CancellationToken));
        await GrainDirectory.Unregister(otherEntry, TestContext.Current.CancellationToken);
        Assert.Equal(expected, await GrainDirectory.Lookup(expected.GrainId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LookupNotFound()
    {
        Assert.Null(await GrainDirectory.Lookup(
            GrainId.Parse("user/somerandomuser_" + Guid.NewGuid().ToString("N")),
            TestContext.Current.CancellationToken));
    }
}
