using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Storage;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 将耐用任务运行时间和信息发送与耐用 grain 类型的启动相连接。
/// </summary>
/// <remarks>
/// <para>
/// 应用 grain 所分类的, <see cref="DurableGrain"/>. 启动机制的建立解决了 grain收件箱和
/// 输出框, 它为 grain提供消息, 并在日记恢复前运行, 所以运行时间的日记勾, 插件
/// 发送信箱信息源在回收状态或恢复之前登记
/// 处理信件。
/// </para>
/// <para>
/// 运行周期的生命周期阶段如下: <see cref="GrainLifecycleStage.Activate"/>,即信息发
/// grain收件箱和发件箱,然后是 <see cref="GrainLifecycleStage.SetupState"/>,该日记在其中
/// 恢复。 因此运行时间恢复任务状态, 一旦日记、 收件箱和发件箱准备好, 并在停止前停止
/// 恢复的处决开始一次
/// 排 grain's <c>OnActivateAsync</c> 已经完成,执行中的处决在处决之前就已中止
/// <c>OnDeactivateAsync</c> 运行;见 <see cref="DurableTaskActivationObserver"/>。
/// </para>
/// <para>
/// 任务状态生活 grain杂志是《美国新闻杂志》的真相来源 grain处决,所以启动
/// 设置需要 grain的日记国家管理器和日记支持的任务存储,并且无法激活
/// 当其中之一失踪时。
/// </para>
/// </remarks>
internal sealed class DurableTaskGrainTypeConfigurator(GrainClassMap grainClasses) : IConfigureGrainTypeComponents
{
    public void Configure(GrainType grainType, GrainProperties properties, GrainTypeSharedContext shared)
    {
        if (!grainClasses.TryGetGrainClass(grainType, out var grainClass)
            || !typeof(DurableGrain).IsAssignableFrom(grainClass))
        {
            return;
        }

        shared.AddActivationSetup(static context =>
        {
            var services = context.ActivationServices;
            ValidateJournaledStorage(services, context.GrainId);
            var runtime = services.GetRequiredService<DurableTaskGrainRuntime>();
            services.GetRequiredService<IJournaledStateManager>()
                .RegisterStateMachine(DurableTaskGrainRuntime.StateMachineName, runtime);
            var handler = services.GetRequiredService<DurableTaskMessageHandler>();
            services.GetRequiredService<IInbox>()
                .Register((IInboxMessageSink)handler)
                .Register((IInboxHandler)handler)
                .RegisterWakeup(runtime.GetWakeupDelay);
            services.GetRequiredService<IOutbox>().Register(runtime);
            context.ObservableLifecycle.Subscribe(
                nameof(DurableTaskGrainRuntime),
                GrainLifecycleStage.Activate + 1,
                runtime.RecoverAsync,
                runtime.StopAsync);
            runtime.InitializeForActivation(context);
        });
    }

    /// <summary>
    /// 确保服务 <paramref name="services"/> 的启动在其日记中保存其任务状态。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 启动没有日记国家经理, 或者它的任务存储不是日记支持的存储。
    /// </exception>
    internal static void ValidateJournaledStorage(IServiceProvider services, GrainId grainId)
    {
        if (services.GetService<IJournaledStateManager>() is null)
        {
            throw new InvalidOperationException(
                $"Durable Tasks stores task state in the grain's journal, and grain '{grainId}' has no journaled state manager. Configure journaling and journal storage on the silo builder.");
        }

        if (services.GetService<IDurableTaskGrainStorage>() is not DurableTaskGrainStorage)
        {
            throw new InvalidOperationException(
                $"Durable Tasks stores task state in the grain's journal, and grain '{grainId}' has no journal-backed task storage. Call AddDurableTasks on the silo builder.");
        }
    }
}
