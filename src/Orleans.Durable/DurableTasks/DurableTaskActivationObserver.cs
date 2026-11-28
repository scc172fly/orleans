using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 持久化 Grain 激活完成后启动激活期间恢复的执行实例，并中止
/// 它们，时机为激活实例开始停用时。
/// </summary>
/// <remarks>
/// <para>
/// Grain 生命周期各阶段在 Grain 的 <c>OnActivateAsync</c> 运行前启动，在其
/// <c>OnDeactivateAsync</c> 之后停止。因此，运行时在其生命周期阶段恢复任务状态，并将 Grain 代码的运行
/// 推迟到激活实例有效时，即激活实例加入工作集时。
/// </para>
/// <para>
/// 开始停用时，运行时停止接受工作，并通知其运行中的执行实例中止，然后
/// 执行 Grain 的 <c>OnDeactivateAsync</c>。随后运行时的生命周期阶段等待已中止的执行实例，再
/// 停止消息传递和日志。
/// </para>
/// <para>
/// 两个回调均在激活实例的调度器上排队执行工作。此观察者适用于
/// <see cref="DurableTaskGrainTypeConfigurator"/> 绑定到运行时的 Grain 类。
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
            // 运行时的生命周期阶段等待同一个停止操作完成。
            context.Scheduler.QueueAction(() => _ = GetRuntime(context).StopAsync(CancellationToken.None));
        }
    }

    private static DurableTaskGrainRuntime GetRuntime(IGrainContext context) =>
        context.ActivationServices.GetRequiredService<DurableTaskGrainRuntime>();
}
