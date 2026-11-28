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
/// 测试 Orleans 的激活取消日志记录行为。
/// 
/// 这些测试验证以下内容：
/// 1. 正常取消激活时（在取消令牌已取消的情况下抛出 OperationCanceledException 或 ObjectDisposedException），
/// 异常会包装为 ActivationCancelledException，
/// 并以 WARNING 级别记录（而非 ERROR 级别）。
/// 2. 出现非取消相关的异常时，仍按预期以 ERROR 级别记录。
/// 
/// 被测试的关键代码位于 ActivationData.cs：
/// - catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) → ActivationCancelledException → Warning 日志
/// - catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested &amp;&amp; !timeout) → ActivationCancelledException → Warning 日志
/// - catch (Exception) → Error 日志
/// 
/// 计划/伪代码（作为注释插入以便追踪）：
/// 1. 添加两个可复用的断言辅助方法：
/// - AssertLogEventExists(logs, eventId) -> 断言 'logs' 中至少有一条日志的 EventId == eventId。
/// - AssertLogEventNotExists(logs, eventId) -> 断言 'logs' 中没有任何日志的 EventId == eventId。
/// 2. 保持原有行为：确保 OperationCanceledException 场景可找到具有激活取消 EventId 的 Info 日志。
/// 3. 另外断言所有错误日志中均不存在 EventId == (int)ErrorCode.Catalog_ErrorCallingActivate 的事件。
/// 4. 辅助方法提供清晰的诊断消息，列出捕获的 EventId 以便调试。
/// 5. 将辅助方法作为测试类上的私有方法，供其他测试复用。
///
/// 下方代码实现这些辅助方法，并在 OperationCanceledException 测试中使用它们。
/// </summary>
[TestArea("Runtime")]
public class ActivationCancellationLoggingTests : OrleansTestingBase, IClassFixture<ActivationCancellationLoggingTests.Fixture>
{
    private readonly Fixture _fixture;
    private readonly ITestOutputHelper _output;

    public class Fixture : BaseTestClusterFixture
    {
        // 声明为静态，以便配置器访问
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
                    // 设置较短的激活超时，以便测试取消场景
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
        
        // 记录所有捕获的条目以便调试
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

    // 可复用辅助方法：断言 'logs' 中至少一条日志具有指定的事件 ID。
    private static void AssertLogEventExists(IEnumerable<LogEntry> logs, int eventId)
    {
        var ids = logs.Select(l => l.EventId.Id).ToList();
        Assert.True(ids.Any(id => id == eventId),
            $"Expected an information log with EventId {eventId} but it was not found. Captured EventIds: {string.Join(',', ids)}");
    }

    // 可复用辅助方法：断言 'logs' 中没有日志具有指定的事件 ID。
    private static void AssertLogEventNotExists(IEnumerable<LogEntry> logs, int eventId)
    {
        var ids = logs.Select(l => l.EventId.Id).ToList();
        Assert.False(ids.Any(id => id == eventId),
            $"Did not expect a log with EventId {eventId}, but it was found. Captured EventIds: {string.Join(',', ids)}");
    }

    #region Cancellation Scenarios - Should NOT log ERROR

    /// <summary>
    /// 如果由于激活期间观察到取消令牌而抛出 OperationCanceledException，
    /// 应以 WARNING 级别记录（而非 ERROR），因为这是有意执行的取消行为。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [Fact, TestCategory("Functional"), TestCategory("ActivationCancellation")]
    public async Task OperationCanceledException_WhenCancellationTokenObserved_LogsInfoNotError()
    {
        Fixture.SharedLoggerProvider.Clear();
        var grain = _fixture.GrainFactory.GetGrain<IActivationCancellation_ThrowsOperationCancelledGrain>(Guid.NewGuid());

        // 设置长于激活超时（2 秒）的延迟以触发取消
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

        // 新增断言：确保存在 EventId == (int)ErrorCode.Catalog_CancelledActivate 的信息级日志。
        AssertLogEventExists(infoLogs, (int)ErrorCode.Catalog_CancelledActivate);

        // 还要确保所有错误日志中均不存在 EventId == (int)ErrorCode.Catalog_ErrorCallingActivate 的事件
        AssertLogEventNotExists(errorLogs, (int)ErrorCode.Catalog_ErrorCallingActivate);
    }

    /// <summary>
    /// 取消之后因服务被释放而抛出 ObjectDisposedException 时，
    /// 应以 WARNING 级别记录（而非 ERROR），因为这是取消期间的预期行为。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [Fact, TestCategory("Functional"), TestCategory("ActivationCancellation")]
    public async Task ObjectDisposedException_WhenCancellationRequested_LogsWarningNotError()
    {
        Fixture.SharedLoggerProvider.Clear();
        var grain = _fixture.GrainFactory.GetGrain<IActivationCancellation_ThrowsObjectDisposedGrain>(Guid.NewGuid());

        // 设置长于激活超时的延迟以触发取消
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


        // 新增断言：确保存在 EventId == (int)ErrorCode.Catalog_CancelledActivate 的信息级日志。 译注：原注释与下方断言不一致；实际断言检查的是 Catalog_DisposedObjectAccess 警告日志。
        AssertLogEventExists(warningLogs, (int)ErrorCode.Catalog_DisposedObjectAccess);

        // 还要确保所有错误日志中均不存在 EventId == (int)ErrorCode.Catalog_ErrorCallingActivate 的事件
        AssertLogEventNotExists(errorLogs, (int)ErrorCode.Catalog_ErrorCallingActivate);
    }

    /// <summary>
    /// 取消期间抛出 TaskCanceledException（继承自 OperationCanceledException）时，
    /// 应以 INFO 级别记录（而非 ERROR）。
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

        // 新增断言：确保存在 EventId == (int)ErrorCode.Catalog_CancelledActivate 的信息级日志。
        AssertLogEventExists(infoLogs, (int)ErrorCode.Catalog_CancelledActivate);

        // 还要确保所有错误日志中均不存在 EventId == (int)ErrorCode.Catalog_ErrorCallingActivate 的事件
        AssertLogEventNotExists(errorLogs, (int)ErrorCode.Catalog_ErrorCallingActivate);
    }

    #endregion

    #region Non-Cancellation Scenarios - SHOULD log ERROR

    /// <summary>
    /// 激活期间抛出普通异常（与取消无关）时，
    /// 应以 ERROR 级别记录，因为这是意外故障。
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

        // 新增断言：确保存在 EventId == (int)ErrorCode.Catalog_CancelledActivate 的信息级日志。 译注：原注释与下方断言不一致；实际断言检查的是 Catalog_ErrorCallingActivate 错误日志。
        AssertLogEventExists(errorLogs, (int)ErrorCode.Catalog_ErrorCallingActivate);
    }

    /// <summary>
    /// 抛出 ObjectDisposedException 但取消令牌并未取消时，
    /// 应以 ERROR 级别记录（'when' 守卫条件不应匹配）。
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

        // 新增断言：确保存在 EventId == (int)ErrorCode.Catalog_CancelledActivate 的信息级日志。 译注：原注释与下方断言不一致；实际断言检查的是 Catalog_ErrorCallingActivate 错误日志。
        AssertLogEventExists(errorLogs, (int)ErrorCode.Catalog_ErrorCallingActivate);
    }

    /// <summary>
    /// 抛出 OperationCanceledException 但取消令牌并未取消时，
    /// 应以 ERROR 级别记录（'when' 守卫条件不应匹配）。
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

        // 新增断言：确保存在 EventId == (int)ErrorCode.Catalog_CancelledActivate 的信息级日志。 译注：原注释与下方断言不一致；实际断言检查的是 Catalog_ErrorCallingActivate 错误日志。
        AssertLogEventExists(errorLogs, (int)ErrorCode.Catalog_ErrorCallingActivate);
    }

    #endregion

    #region Baseline Tests - Normal Operation

    /// <summary>
    /// 基线测试：成功激活不应记录任何错误或警告。
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

        // 新增断言：确保存在 EventId == (int)ErrorCode.Catalog_CancelledActivate 的信息级日志。 译注：原注释与下方断言不一致；实际断言要求不存在 Catalog_CancelledActivate 信息日志。
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
