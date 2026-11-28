#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Orleans.Hosting;
using Orleans.Reminders;
using Orleans.TestingHost;

namespace Orleans.Testing.Reminders;

/// <summary>
/// 为在 <see cref="InProcessTestCluster"/> 中运行的提醒器测试提供确定性时间控制。
/// </summary>
/// <remarks>
/// 在构建集群之前将实例附加到 <see cref="InProcessTestClusterBuilder"/>。
/// 附加的时钟会替换提醒器子系统的键控 <see cref="TimeProvider"/>，并调整
/// <see cref="ReminderOptions"/> 以实现确定性的提醒器调度，同时不影响无关的 Silo 定时器。
/// </remarks>
public sealed class ReminderTestClock : IDisposable
{
    private readonly SemaphoreSlim _advanceLock = new(1, 1);
    private bool _disposed;

    private ReminderTestClock(
        InProcessTestClusterBuilder builder,
        DateTimeOffset initialTime,
        TimeSpan minimumReminderPeriod,
        TimeSpan refreshReminderListPeriod,
        TimeSpan initializationTimeout)
    {
        TimeProvider = new FakeTimeProvider(initialTime);
        DiagnosticObserver = ReminderDiagnosticObserver.Create(builder);
        MinimumReminderPeriod = minimumReminderPeriod;
        RefreshReminderListPeriod = refreshReminderListPeriod;
        InitializationTimeout = initializationTimeout;
    }

    internal FakeTimeProvider TimeProvider { get; }

    /// <summary>
    /// 获取在构建集群之前附加的诊断观察者，使提醒器服务启动事件
    /// 可供确定性生命周期测试使用。
    /// </summary>
    public ReminderDiagnosticObserver DiagnosticObserver { get; }

    /// <summary>
    /// 获取当前提醒器时钟时间。
    /// </summary>
    public DateTimeOffset UtcNow => TimeProvider.GetUtcNow();

    /// <summary>
    /// 获取使用此时钟的集群所配置的最短提醒器周期。
    /// </summary>
    public TimeSpan MinimumReminderPeriod { get; }

    /// <summary>
    /// 获取使用此时钟的集群所配置的提醒器表刷新周期。
    /// </summary>
    public TimeSpan RefreshReminderListPeriod { get; }

    /// <summary>
    /// 获取使用此时钟的集群所配置的提醒器服务初始化超时时间。
    /// </summary>
    public TimeSpan InitializationTimeout { get; }

    /// <summary>
    /// 将确定性提醒器时钟附加到指定的 <see cref="InProcessTestClusterBuilder"/>。
    /// </summary>
    /// <param name="builder">测试集群构建器。</param>
    /// <param name="minimumReminderPeriod">可选的最短提醒器周期覆盖值。</param>
    /// <param name="refreshReminderListPeriod">可选的提醒器列表刷新周期覆盖值。</param>
    /// <param name="initializationTimeout">可选的提醒器初始化超时覆盖值。</param>
    /// <returns>已附加的提醒器测试时钟。</returns>
    public static ReminderTestClock Attach(
        InProcessTestClusterBuilder builder,
        TimeSpan? minimumReminderPeriod = null,
        TimeSpan? refreshReminderListPeriod = null,
        TimeSpan? initializationTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var clock = new ReminderTestClock(
            builder,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            minimumReminderPeriod ?? TimeSpan.FromMinutes(1),
            refreshReminderListPeriod ?? TimeSpan.FromSeconds(1),
            initializationTimeout ?? TimeSpan.FromSeconds(30));

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.Services.AddKeyedSingleton<TimeProvider>(ReminderTimeProviderNames.Reminders, clock.TimeProvider);
            siloBuilder.Services.PostConfigure<ReminderOptions>(options =>
            {
                options.MinimumReminderPeriod = clock.MinimumReminderPeriod;
                options.RefreshReminderListPeriod = clock.RefreshReminderListPeriod;
                options.InitializationTimeout = clock.InitializationTimeout;
            });
        });

        return clock;
    }

    /// <summary>
    /// 将集群提醒器时钟推进指定时长。
    /// </summary>
    /// <param name="amount">要推进的时长。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task AdvanceAsync(TimeSpan amount, CancellationToken cancellationToken = default)
    {
        if (amount < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The advance amount must not be negative.");
        }

        await _advanceLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            TimeProvider.Advance(amount);
        }
        finally
        {
            _advanceLock.Release();
        }

        await Task.Yield();
    }

    /// <summary>
    /// 阻止并发推进时钟，直到返回的句柄被释放。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>一个支持异步释放的句柄，释放时恢复时钟推进。</returns>
    public async Task<IAsyncDisposable> FreezeAsync(CancellationToken cancellationToken = default)
    {
        await _advanceLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            return new AsyncLockRelease(_advanceLock);
        }
        catch
        {
            _advanceLock.Release();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DiagnosticObserver.Dispose();
        _advanceLock.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class AsyncLockRelease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore = semaphore;
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
