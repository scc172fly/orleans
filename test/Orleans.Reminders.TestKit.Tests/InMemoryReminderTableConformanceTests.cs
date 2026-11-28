using Orleans.Hosting;
using Orleans.Reminders.TestKit;
using Xunit;

namespace Orleans.Reminders.TestKit.Tests;

/// <summary>
/// 在进程内集群中部署内置的内存（基于 Grain）提醒器提供程序。
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
/// 对内置内存提醒器提供程序运行共享的直接一致性测试套件。
/// </summary>
/// <remarks>
/// 它是唯一无需外部服务即可测试的内置提醒器提供程序，因此是
/// TestKit 的参考集成方式。其他内置提供程序在其后端服务可用时，
/// 通过各自的测试夹具采用同一套件。
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
