#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.ActivationCancellationTests;

/// <summary>
/// Orleans中激活取消记录行为的测试。
/// 
/// 这些测试证实:
/// 1. 当启动被优雅地取消时(OperationCanceledException 或 ObjectDisposedException)
///    挂起取消标记时, 取消标记时将取消, 例外部分在 ActivationCancelledException中包装
///    记录在警告级别(而不是错误级别)。
/// 2. 如果出现非取消性例外,仍按预期记录在紧急风险水平。
/// 
/// 正在测试的关键代码在 ActivationData.cs中:
/// - 渔获(ObjectDisposedException) 时间(cancellationToken.IsCancellationRequested) → ActivationCancelledException 警报日志
/// - 渔获(OperationCanceledException) 时间(cancellationToken.IsCancellationRequested &amp;&amp; !timeout) → ActivationCancelledException 警报日志
/// - 误差日志
/// 
/// 计划/替代代码(插入作为可追踪性评论):
/// 1. 增加两个可重复使用索赔帮助人:
///    - AssertLogEventExists(logs, eventId) - > 称任何“logs”中的日志都有 EventId = eventId。
///    - AssertLogEventNotExists(logs, eventId) - > 称“logs”中没有日志 EventId = eventId
/// 2. 保持现有行为:确保 OperationCanceledException 假设找到与取消活动 EventId有关的信息日志。
/// 3. 此外,还声称,跨误差日志没有发生任何事件 EventId == (int)ErrorCode.Catalog_ErrorCallingActivate.
/// 4. 帮助者包括清晰的诊断信息,列出所捕捉到的 EventIds ,以便于调试。
/// 5. 在试验类中将辅助人员作为私人方法放置,以便其他试验可以重新使用。
///
/// 以下代码执行这些辅助人员,并在 OperationCanceledException 测试中使用。
/// </summary>
[TestArea("Runtime")]
public class ActivationCancellationLoggingTests : OrleansTestingBase, IClassFixture<ActivationCancellationLoggingTests.Fixture>
{
    private readonly Fixture _fixture;
    private readonly ITestOutputHelper _output;

    public class Fixture : BaseTestClusterFixture
    {
        // 静态,以便从配置器中访问
        public static InMemoryLoggerProvider SharedLoggerProvider { get; } = new();

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.Options.InitialSilosCount = 1;
            builder.AddSiloBuilderConfigurator<SiloHostConfigurator>();
        }

        private class SiloHostConfigurator : ISiloConfigurator, IHostConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder.Configure<GrainCollectionOptions>(options =>
                {
                    options.CollectionAge = TimeSpan.FromSeconds(10);
                    options.CollectionQuantum = TimeSpan.FromSeconds(1);
                    // 短时间激活超时,以便测试取消方案
                    options.ActivationTimeout = TimeSpan.FromSeconds(2);
                    options.DeactivationTimeout = TimeSpan.FromSeconds(2);
                });

                hostBuilder.Configure<SiloMessagingOptions>(options =>
                {
                    options.MaxRequestProcessingTime = TimeSpan.FromSeconds(5);
                });
            }

            public void Configure(IHostBuilder hostBuilder)
            {
                hostBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<ILoggerProvider>(SharedLoggerProvider);
                });
            }
        }
    }

    public ActivationCancellationLoggingTests(Fixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        Fixture.SharedLoggerProvider.Clear();
    }

    private (IReadOnlyList<LogEntry> ErrorLogs, IReadOnlyList<LogEntry> WarningLogs, IReadOnlyList<LogEntry> InfoLogs) GetActivationLogs()
    {
        var logs = Fixture.SharedLoggerProvider.GetLogs();
        
        // 记录所有已抓取的调试条目
        _output.WriteLine($"Total logs captured: {logs.Count}");
        foreach (var log in logs.Where(l => l.Level >= LogLevel.Warning))
        {
            _output.WriteLine($"[{log.Level}] [{log.Category}] {log.Message}");
        }

        // 查找与激活相关的日志 
        var activationLogs = logs.Where(l => 
            l.Category.Contains("Orleans.Grain") || 
            l.Category.Contains("ActivationData") ||
            l.Message.Contains("Activation", StringComparison.OrdinalIgnoreCase) ||
            l.Message.Contains("activating", StringComparison.OrdinalIgnoreCase)).ToList();

        var errorLogs = activationLogs.Where(l => l.Level == LogLevel.Error).ToList();
        var warningLogs = activationLogs.Where(l => l.Level == LogLevel.Warning).ToList();
        var infoLogs = activationLogs.Where(l => l.Level == LogLevel.Information).ToList();

        return (errorLogs, warningLogs, infoLogs);
    }

    // 可重复使用助手: 声明“ logs” 中至少有一个日志有指定的事件代号 。
    private static void AssertLogEventExists(IEnumerable<LogEntry> logs, int eventId)
    {
        var ids = logs.Select(l => l.EventId.Id).ToList();
        Assert.True(ids.Any(id => id == eventId),
            $"Expected an information log with EventId {eventId} but it was not found. Captured EventIds: {string.Join(',', ids)}");
    }

    // 可重复使用助手: 声明“ logs” 没有指定事件代号的日志 。
    private static void AssertLogEventNotExists(IEnumerable<LogEntry> logs, int eventId)
    {
        var ids = logs.Select(l => l.EventId.Id).ToList();
        Assert.False(ids.Any(id => id == eventId),
            $"Did not expect a log with EventId {eventId}, but it was found. Captured EventIds: {string.Join(',', ids)}");
    }

    #region Cancellation Scenarios - Should NOT log ERROR

    /// <summary>
    /// 当扔出 OperationCanceledException 是因为在激活期间观察到了取消牌,
    /// 因为它是故意取消行为。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [Fact, TestCategory("Functional"), TestCategory("ActivationCancellation")]
    public async Task OperationCanceledException_WhenCancellationTokenObserved_LogsInfoNotError()
    {
        Fixture.SharedLoggerProvider.Clear();
        var grain = _fixture.GrainFactory.GetGrain<IActivationCancellation_ThrowsOperationCancelledGrain>(Guid.NewGuid());

        // 设定比激活超时(2秒)延长的延迟时间以触发取消
        RequestContext.Set("delay_activation_ms", 5000);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await grain.IsActivated().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        });

        RequestContext.Clear();

        await Task.Delay(100, TestContext.Current.CancellationToken);

        var (errorLogs, warningLogs, infoLogs) = GetActivationLogs();

        Assert.Empty(errorLogs);
        Assert.Empty(warningLogs);
        Assert.NotEmpty(infoLogs);

        // 新主张:确保有 EventId y y y y (int)ErrorCode.Catalog_CancelledActivate的信息日志
        AssertLogEventExists(infoLogs, (int)ErrorCode.Catalog_CancelledActivate);

        // 还要确保没有发生 EventId [[int]ErrorCode.Catalog_ErrorCallingActivate的错误日志
        AssertLogEventNotExists(errorLogs, (int)ErrorCode.Catalog_ErrorCallingActivate);
    }

    /// <summary>
    /// 当 ObjectDisposedException 因服务取消后处置而抛出时,
    /// 记录在警告级别(而不是错误),因为这是取消时预期的行为。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [Fact, TestCategory("Functional"), TestCategory("ActivationCancellation")]
    public async Task ObjectDisposedException_WhenCancellationRequested_LogsWarningNotError()
    {
        Fixture.SharedLoggerProvider.Clear();
        var grain = _fixture.GrainFactory.GetGrain<IActivationCancellation_ThrowsObjectDisposedGrain>(Guid.NewGuid());

        // 设定比激活超时延长的延迟时间触发取消
        RequestContext.Set("delay_activation_ms", 5000);

        await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () =>
        {
            await grain.IsActivated();
        });

        RequestContext.Clear();

        await Task.Delay(100, TestContext.Current.CancellationToken);

        var (errorLogs, warningLogs, infoLogs) = GetActivationLogs();

        Assert.Empty(errorLogs);
        Assert.NotEmpty(warningLogs);


        // 新主张:确保有 EventId y y y y (int)ErrorCode.Catalog_CancelledActivate的信息日志
        AssertLogEventExists(warningLogs, (int)ErrorCode.Catalog_DisposedObjectAccess);

        // 还要确保没有发生 EventId [[int]ErrorCode.Catalog_ErrorCallingActivate的错误日志
        AssertLogEventNotExists(errorLogs, (int)ErrorCode.Catalog_ErrorCallingActivate);
    }

    /// <summary>
    /// 何时 TaskCanceledException (继承人继承人继承的遗产) OperationCanceledException)在取消时被丢弃,
    /// 应在INFO一级(而不是错误)记录。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [Fact, TestCategory("Functional"), TestCategory("ActivationCancellation")]
    public async Task TaskCanceledException_WhenCancellationRequested_LogsInfoNotError()
    {
        Fixture.SharedLoggerProvider.Clear();
        var grain = _fixture.GrainFactory.GetGrain<IActivationCancellation_ThrowsTaskCancelledGrain>(Guid.NewGuid());

        RequestContext.Set("delay_activation_ms", 5000);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await grain.IsActivated().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        });

        RequestContext.Clear();

        await Task.Delay(100, TestContext.Current.CancellationToken);

        var (errorLogs, warningLogs, infoLogs) = GetActivationLogs();

        Assert.Empty(errorLogs);
        Assert.Empty(warningLogs);
        Assert.NotEmpty(infoLogs);

        // 新主张:确保有 EventId y y y y (int)ErrorCode.Catalog_CancelledActivate的信息日志
        AssertLogEventExists(infoLogs, (int)ErrorCode.Catalog_CancelledActivate);

        // 还要确保没有发生 EventId [[int]ErrorCode.Catalog_ErrorCallingActivate的错误日志
        AssertLogEventNotExists(errorLogs, (int)ErrorCode.Catalog_ErrorCallingActivate);
    }

    #endregion

    #region Non-Cancellation Scenarios - SHOULD log ERROR

    /// <summary>
    /// 当在激活期间提出一般例外(与取消无关)时,
    /// 应在错误级别上记录,因为这是意外的失败。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("ActivationCancellation")]
    public async Task GenericException_DuringActivation_LogsError()
    {
        Fixture.SharedLoggerProvider.Clear();
        var grain = _fixture.GrainFactory.GetGrain<IActivationCancellation_ThrowsGenericExceptionGrain>(Guid.NewGuid());

        RequestContext.Set("throw_exception", true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.IsActivated());

        RequestContext.Clear();

        await Task.Delay(100, TestContext.Current.CancellationToken);

        var (errorLogs, _, infoLogs) = GetActivationLogs();

        Assert.NotEmpty(errorLogs);

        // 新主张:确保有 EventId y y y y (int)ErrorCode.Catalog_CancelledActivate的信息日志
        AssertLogEventExists(errorLogs, (int)ErrorCode.Catalog_ErrorCallingActivate);
    }

    /// <summary>
    /// 当抛出 ObjectDisposedException 时, 取消的牌子没有被注销,
    /// 记录在错误级别(“何时”警卫不能匹配)。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("ActivationCancellation")]
    public async Task ObjectDisposedException_WhenNotCancelled_LogsError()
    {
        Fixture.SharedLoggerProvider.Clear();
        var grain = _fixture.GrainFactory.GetGrain<IActivationCancellation_ThrowsObjectDisposedUnconditionallyGrain>(Guid.NewGuid());

        RequestContext.Set("throw_object_disposed", true);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => grain.IsActivated());

        RequestContext.Clear();

        await Task.Delay(100, TestContext.Current.CancellationToken);

        var (errorLogs, _, infoLogs) = GetActivationLogs();

        Assert.NotEmpty(errorLogs);

        // 新主张:确保有 EventId y y y y (int)ErrorCode.Catalog_CancelledActivate的信息日志
        AssertLogEventExists(errorLogs, (int)ErrorCode.Catalog_ErrorCallingActivate);
    }

    /// <summary>
    /// 当抛出 OperationCanceledException 时, 取消的牌子没有被注销,
    /// 记录在错误级别(“何时”警卫不能匹配)。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("ActivationCancellation")]
    public async Task OperationCanceledException_WhenNotCancelled_LogsError()
    {
        Fixture.SharedLoggerProvider.Clear();
        var grain = _fixture.GrainFactory.GetGrain<IActivationCancellation_ThrowsOperationCancelledUnconditionallyGrain>(Guid.NewGuid());

        RequestContext.Set("throw_operation_cancelled", true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => grain.IsActivated());

        RequestContext.Clear();

        await Task.Delay(100, TestContext.Current.CancellationToken);

        var (errorLogs, _, infoLogs) = GetActivationLogs();

        Assert.NotEmpty(errorLogs);

        // 新主张:确保有 EventId y y y y (int)ErrorCode.Catalog_CancelledActivate的信息日志
        AssertLogEventExists(errorLogs, (int)ErrorCode.Catalog_ErrorCallingActivate);
    }

    #endregion

    #region Baseline Tests - Normal Operation

    /// <summary>
    /// 基准测试: 成功激活时不应记录任何错误或警告 。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("ActivationCancellation")]
    public async Task SuccessfulActivation_NoErrorOrWarningLogs()
    {
        Fixture.SharedLoggerProvider.Clear();
        var grain = _fixture.GrainFactory.GetGrain<IActivationCancellation_SuccessfulActivationGrain>(Guid.NewGuid());

        var isActivated = await grain.IsActivated();

        await Task.Delay(100, TestContext.Current.CancellationToken);

        var (errorLogs, warningLogs, infoLogs) = GetActivationLogs();

        Assert.True(isActivated);
        Assert.Empty(errorLogs);
        Assert.Empty(warningLogs);

        // 新主张:确保有 EventId y y y y (int)ErrorCode.Catalog_CancelledActivate的信息日志
        AssertLogEventNotExists(infoLogs, (int)ErrorCode.Catalog_CancelledActivate);
    }

    #endregion
}

public class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly List<LogEntry> _logs = new();
    private readonly object _lock = new();

    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(this, categoryName);

    public void Dispose() { }

    public void Clear()
    {
        lock (_lock)
        {
            _logs.Clear();
        }
    }

    public IReadOnlyList<LogEntry> GetLogs()
    {
        lock (_lock)
        {
            return _logs.ToList();
        }
    }

    internal void AddLog(LogEntry entry)
    {
        lock (_lock)
        {
            _logs.Add(entry);
        }
    }

    private class InMemoryLogger(InMemoryLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            provider.AddLog(new LogEntry(categoryName, logLevel, eventId, formatter(state, exception), exception));
        }
    }
}

public record LogEntry(string Category, LogLevel Level, EventId EventId, string Message, Exception? Exception);
