using Orleans.Runtime;

namespace Timers;

// Grain 定时器用法的文档示例。

// <grain_timer_example>
public class MyGrain : Grain, IMyGrain
{
    private IGrainTimer? _timer;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // 创建定时器，在激活后 5 秒首次触发，此后每 10 秒触发一次。
        // RegisterGrainTimer 是 IGrainBase（由 Grain 实现）的扩展方法。
        _timer = this.RegisterGrainTimer(
            static (state, ct) => state.DoWorkAsync(ct),
            this,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromSeconds(5),
                Period = TimeSpan.FromSeconds(10),
                KeepAlive = true  // 定时器处于活动状态时阻止 Grain 被回收。
            });

        return Task.CompletedTask;
    }

    private Task DoWorkAsync(CancellationToken cancellationToken)
    {
        // 定时器回调工作。
        return Task.CompletedTask;
    }
}
// </grain_timer_example>

// <migrate_after>
public class MyGrainAfter : Grain, IMyGrain
{
    private IGrainTimer? _timer;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // 使用 this.RegisterGrainTimer()，它是 IGrainBase 的扩展方法。
        _timer = this.RegisterGrainTimer(
            static (state, ct) => state.DoWorkAsync(ct),
            this,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromSeconds(5),
                Period = TimeSpan.FromSeconds(10),
                Interleave = true  // 设置为 true 以匹配旧行为。
            });
        
        return Task.CompletedTask;
    }

    private Task DoWorkAsync(CancellationToken cancellationToken)
    {
        // 定时器工作：检查 cancellationToken 以实现优雅关闭。
        return Task.CompletedTask;
    }
}
// </migrate_after>

public interface IMyGrain : IGrainWithGuidKey
{
}
