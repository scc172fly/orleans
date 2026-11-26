#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Orleans.Hosting;
using Orleans.Reminders;
using Orleans.TestingHost;

namespace Orleans.Testing.Reminders;

/// <summary>
/// 为在 <see cref="InProcessTestCluster"/>运行的提醒测试提供确定时间控制。
/// </summary>
/// <remarks>
/// 在构建数据组之前,将实例附加到 <see cref="InProcessTestClusterBuilder"/> 。
/// 随附时钟替换提醒子系统的关键键 <see cref="TimeProvider"/> 和调
/// 确定性提醒时间安排的<see cref="ReminderOptions"/> ,不影响无关的 silo 计时器。
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
    /// 获得在集群组建立前附的诊断观察家,因此提醒服务启动活动
    /// 可用于确定性生命周期测试。
    /// </summary>
    public ReminderDiagnosticObserver DiagnosticObserver { get; }

    /// <summary>
    /// 获取当前提醒时钟的时间 。
    /// </summary>
    public DateTimeOffset UtcNow => TimeProvider.GetUtcNow();

    /// <summary>
    /// 获得使用此时钟配置的集群最小提醒时间段 。
    /// </summary>
    public TimeSpan MinimumReminderPeriod { get; }

    /// <summary>
    /// 获取使用此时钟配置的提醒表更新周期。
    /// </summary>
    public TimeSpan RefreshReminderListPeriod { get; }

    /// <summary>
    /// 获取使用此时钟配置的提醒服务初始化超时,用于集组。
    /// </summary>
    public TimeSpan InitializationTimeout { get; }

    /// <summary>
    /// 在所提供的 <see cref="InProcessTestClusterBuilder"/>上附上确定性提醒时钟。
    /// </summary>
    /// <param name="builder">测试组群构建器。</param>
    /// <param name="minimumReminderPeriod">可选择的最低提醒时间过重 。</param>
    /// <param name="refreshReminderListPeriod">提醒列表的复新期替换。</param>
    /// <param name="initializationTimeout">提醒启动超时覆盖选项。</param>
    /// <returns>所附的提醒测试钟。</returns>
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
    /// 以指定数额将集束提醒时钟提前。
    /// </summary>
    /// <param name="amount">预发时间的长度 。</param>
    /// <param name="cancellationToken">取消的符号。</param>
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
    /// 防止同时时预付,直到处理回转的把手。
    /// </summary>
    /// <param name="cancellationToken">取消的符号。</param>
    /// <returns>一个可自动处理的处理器, 可在处置时恢复钟速预发款。</returns>
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
