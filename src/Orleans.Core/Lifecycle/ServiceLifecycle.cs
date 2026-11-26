using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Orleans;

/// <summary>
/// 允许消费者观察和参与客户/silo的生命周期。
/// </summary>
public interface IServiceLifecycle
{
    /// <summary>
    /// 当客户端/silo 完全启动并准备接受流量时触发。
    /// </summary>
    IServiceLifecycleStage Started { get; }

    /// <summary>
    /// 当客户端/silo 开始关闭进程时触发 。
    /// </summary>
    IServiceLifecycleStage Stopping { get; }

    /// <summary>
    /// 当客户端/silo 完成关闭程序时触发。
    /// </summary>
    IServiceLifecycleStage Stopped { get; }
}

internal sealed class ServiceLifecycle<TLifecycleObservable>(ILogger<ServiceLifecycle<TLifecycleObservable>> logger) :
    IServiceLifecycle, ILifecycleParticipant<TLifecycleObservable>, IDisposable
        where TLifecycleObservable : ILifecycleObservable
{
    private readonly ServiceLifecycleNotificationStage _started = new(logger, "Started");
    private readonly ServiceLifecycleNotificationStage _stopping = new(logger, "Stopping");
    private readonly ServiceLifecycleNotificationStage _stopped = new(logger, "Stopped");

    public IServiceLifecycleStage Started => _started;
    public IServiceLifecycleStage Stopping => _stopping;
    public IServiceLifecycleStage Stopped => _stopped;

    public void Dispose()
    {
        _started.Dispose();
        _stopping.Dispose();
        _stopped.Dispose();
    }

    public void Participate(TLifecycleObservable lifecycle)
    {
        lifecycle.Subscribe(
            observerName: nameof(Started),
            stage: ServiceLifecycleStage.Active,
            onStart: _started.NotifyCompleted,
            onStop: NoOpStop);

        lifecycle.Subscribe(
            observerName: nameof(Stopping),
            stage: ServiceLifecycleStage.Active,
            onStart: NoOpStart,
            onStop: _stopping.NotifyCompleted);

        lifecycle.Subscribe(
            observerName: nameof(Stopped),
            stage: ServiceLifecycleStage.RuntimeInitialize - 1,
            onStart: NoOpStart,
            onStop: _stopped.NotifyCompleted);

        static Task NoOpStart(CancellationToken _) => Task.CompletedTask;
        static Task NoOpStop(CancellationToken _) => Task.CompletedTask;
    }
}
