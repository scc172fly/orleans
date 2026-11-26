using Orleans.Hosting;
using Orleans.Reminders.TestKit;
using Xunit;

namespace Orleans.Reminders.TestKit.Tests;

/// <summary>
/// 部署内置的记忆( 内部) (O)grain提醒在程序内集群中的提供者。
/// </summary>
public sealed class InMemoryReminderTableFixture : ReminderTableTestFixture, IAsyncLifetime
{
    protected override void ConfigureSilo(ISiloBuilder siloBuilder) => siloBuilder.UseInMemoryReminderService();

    public override ValueTask InitializeAsync() => base.InitializeAsync(TestContext.Current.CancellationToken);

    public override async ValueTask DisposeAsync()
    {
        using var cleanupCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await base.DisposeAsync(cleanupCancellation.Token);
    }
}

/// <summary>
/// 运行共享的直接兼容套件,与内置的记忆提醒提供商相对应。
/// </summary>
/// <remarks>
/// 这是唯一一个内置提醒提供人,无需外部服务即可使用,因此,它就是
/// 和《京都议定书》 TestKit。其他内置提供商通过自己的服务来采用相同的套件套件
/// 一旦它们有后援服务,就立即提供固定装置。
/// </remarks>
[TestSuite("BVT")]
[TestProvider("None")]
[TestCategory("BVT"), TestCategory("Reminders"), TestCategory("ReminderTestKit")]
public sealed class InMemoryReminderTableConformanceTests : ReminderTableTestRunner, IClassFixture<InMemoryReminderTableFixture>
{
    private readonly InMemoryReminderTableFixture _fixture;

    public InMemoryReminderTableConformanceTests(InMemoryReminderTableFixture fixture)
        : base(fixture.ReminderTable, "InMemoryReminderTable")
    {
        _fixture = fixture;
        _fixture.EnsurePreconditionsMet();
    }

    [Fact]
    public override Task ReminderTable_StartAsync_IsIdempotent() => base.RunReminderTable_StartAsync_IsIdempotent(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_StopAsync_ThenRestart_ResumesService() => base.RunReminderTable_StopAsync_ThenRestart_ResumesService(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_UpsertRow_ReturnsNewNonEmptyETag() => base.RunReminderTable_UpsertRow_ReturnsNewNonEmptyETag(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_UpsertRow_PersistsScheduleForPointRead() => base.RunReminderTable_UpsertRow_PersistsScheduleForPointRead(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_ReadRow_MissingReminder_ReturnsNull() => base.RunReminderTable_ReadRow_MissingReminder_ReturnsNull(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_ReadRows_ForGrain_ReturnsOnlyThatGrainsReminders() => base.RunReminderTable_ReadRows_ForGrain_ReturnsOnlyThatGrainsReminders(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_ReadRows_ForUnknownGrain_ReturnsEmpty() => base.RunReminderTable_ReadRows_ForUnknownGrain_ReturnsEmpty(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_Identity_IsGrainIdAndReminderName() => base.RunReminderTable_Identity_IsGrainIdAndReminderName(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_Identity_WithSpecialCharacters_RoundTrips() => base.RunReminderTable_Identity_WithSpecialCharacters_RoundTrips(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_UpsertRow_ReplacesETagOnEachWrite() => base.RunReminderTable_UpsertRow_ReplacesETagOnEachWrite(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_RemoveRow_WithCurrentETag_RemovesRow() => base.RunReminderTable_RemoveRow_WithCurrentETag_RemovesRow(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_RemoveRow_WithStaleETag_FailsAndRetainsRow() => base.RunReminderTable_RemoveRow_WithStaleETag_FailsAndRetainsRow(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_RemoveRow_WithUnknownReminderName_ReturnsFalse() => base.RunReminderTable_RemoveRow_WithUnknownReminderName_ReturnsFalse(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_RemoveRow_Repeated_ReturnsFalseAfterFirstSuccess() => base.RunReminderTable_RemoveRow_Repeated_ReturnsFalseAfterFirstSuccess(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_UpsertRow_UpdatesStartAtAndPeriod() => base.RunReminderTable_UpsertRow_UpdatesStartAtAndPeriod(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_UpsertRow_MovesReminderBetweenLoadingWindows() => base.RunReminderTable_UpsertRow_MovesReminderBetweenLoadingWindows(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_ReadRows_FullRange_ReturnsAllReminders() => base.RunReminderTable_ReadRows_FullRange_ReturnsAllReminders(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_ReadRows_UnsignedBoundary_UsesUInt32Ordering()
        => base.RunReminderTable_ReadRows_UnsignedBoundary_UsesUInt32Ordering(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_ReadRows_Range_ExcludesBeginAndIncludesEnd() => base.RunReminderTable_ReadRows_Range_ExcludesBeginAndIncludesEnd(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_ReadRows_WrapAroundRange_ReturnsWrappedSegment() => base.RunReminderTable_ReadRows_WrapAroundRange_ReturnsWrappedSegment(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_ReadRows_OutsideRange_DoesNotDeleteReminder() => base.RunReminderTable_ReadRows_OutsideRange_DoesNotDeleteReminder(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_ReadRows_AfterRemoval_OmitsRemovedReminder() => base.RunReminderTable_ReadRows_AfterRemoval_OmitsRemovedReminder(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_ReadRow_AfterRemoval_ReturnsNull() => base.RunReminderTable_ReadRow_AfterRemoval_ReturnsNull(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_ConcurrentUpserts_ProduceDistinctETags() => base.RunReminderTable_ConcurrentUpserts_ProduceDistinctETags(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_ParallelUpserts_AcrossGrains_RemainIsolated() => base.RunReminderTable_ParallelUpserts_AcrossGrains_RemainIsolated(TestContext.Current.CancellationToken);

    [Fact]
    public override Task ReminderTable_TestOnlyClearTable_RemovesAllReminders() => base.RunReminderTable_TestOnlyClearTable_RemovesAllReminders(TestContext.Current.CancellationToken);

    [Fact]
    public Task InMemoryReminderTable_FullRangeReturnsExactCardinality()
        => base.RunReminderTable_ReadRows_FullRange_ReturnsExactRequestedCardinality(32, TestContext.Current.CancellationToken);

    [Fact, TestCategory("ModelBased")]
    public Task InMemoryReminderTable_ModelBasedGeneratedConformance()
        => new ReminderTableModelBasedTestRunner(ReminderTable, "InMemoryReminderTable").RunGeneratedConformanceTests(TestContext.Current.CancellationToken);
}
