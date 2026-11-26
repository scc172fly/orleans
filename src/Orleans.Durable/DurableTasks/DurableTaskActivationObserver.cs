using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 开始处决,一旦启动完成后,耐用 grain的激活即恢复并中止
/// 当激活开始停止活动时
/// </summary>
/// <remarks>
/// <para>
/// Grain 生命周期阶段在 grain's <c>OnActivateAsync</c> 运行后停止
/// <c>OnDeactivateAsync</c>因此运行时间在生命周期阶段恢复任务状态,推迟运行
/// grain 代码,直到激活有效,即当激活加入工作组时。
/// </para>
/// <para>
/// 当停止工作开始时,运行时间停止接受工作,并信号在工作开始前中止执行处决
/// grain's <c>OnDeactivateAsync</c> 运行。运行运行中,运行时间的生命周期阶段之后等待被中止的处决,然后在
/// 和日记停止。
/// </para>
/// <para>
/// 两种回调都排队在激活调度器上进行工作。 观察家对 grain 类应用
/// <see cref="DurableTaskGrainTypeConfigurator"/> 绑定在运行时间 。
/// </para>
/// </remarks>
internal sealed class DurableTaskActivationObserver : IActivationWorkingSetObserver
{
    public void OnAdded(IActivationWorkingSetMember member)
    {
        if (member is IGrainContext { GrainInstance: DurableGrain } context)
        {
            context.Scheduler.QueueAction(() => GetRuntime(context).StartRecoveredExecutions());
        }
    }

    public void OnDeactivating(IActivationWorkingSetMember member)
    {
        if (member is IGrainContext { GrainInstance: DurableGrain } context)
        {
            // 运行周期的生命周期阶段 等待同样的停止。
            context.Scheduler.QueueAction(() => _ = GetRuntime(context).StopAsync(CancellationToken.None));
        }
    }

    private static DurableTaskGrainRuntime GetRuntime(IGrainContext context) =>
        context.ActivationServices.GetRequiredService<DurableTaskGrainRuntime>();
}
