using Orleans.Runtime;

namespace Timers;

// grain 定时器使用文档示例

// <grain_timer_example>
public class MyGrain : Grain, IMyGrain
{
    private IGrainTimer? _timer;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // 创建一个定时器,每10秒点火,启动后5秒钟
        // RegisterGrainTimer 是 IGrainBase 的扩展方法(它 Grain 实施)
        _timer = this.RegisterGrainTimer(
            static (state, ct) => state.DoWorkAsync(ct),
            this,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromSeconds(5),
                Period = TimeSpan.FromSeconds(10),
                KeepAlive = true  // 防止在计时器激活时进行 grain 收藏
            });

        return Task.CompletedTask;
    }

    private Task DoWorkAsync(CancellationToken cancellationToken)
    {
        // 计时回调工作
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
        // 使用 this.RegisterGrainTimer() - IGrainBase的扩展方法
        _timer = this.RegisterGrainTimer(
            static (state, ct) => state.DoWorkAsync(ct),
            this,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromSeconds(5),
                Period = TimeSpan.FromSeconds(10),
                Interleave = true  // 设置真实以匹配旧行为
            });
        
        return Task.CompletedTask;
    }

    private Task DoWorkAsync(CancellationToken cancellationToken)
    {
        // 计时器工作 - 检查优雅关闭的 cancellationToken
        return Task.CompletedTask;
    }
}
// </migrate_after>

public interface IMyGrain : IGrainWithGuidKey
{
}
