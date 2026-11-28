using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Storage;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 将持久化任务运行时和消息传递绑定到持久化 Grain 类型的激活实例。
/// </summary>
/// <remarks>
/// <para>
/// 适用于派生自 <see cref="DurableGrain"/> 的 Grain 类。激活设置解析该 Grain 的收件箱和
/// 发件箱，从而启用其消息传递功能，并在日志恢复之前运行，因此运行时的日志钩子、收件箱
/// 接收器、处理程序及唤醒延迟，以及发件箱消息源，都会在处理已恢复的状态或已恢复的
/// 消息之前完成注册。
/// </para>
/// <para>
/// 运行时的生命周期阶段在 <see cref="GrainLifecycleStage.Activate"/> 之后，该阶段中消息传递启动
/// Grain 的收件箱和发件箱；该阶段又在 <see cref="GrainLifecycleStage.SetupState"/> 之后，后者执行日志
/// 恢复。因此，运行时在日志、收件箱和发件箱均就绪后恢复任务状态，并在
/// 消息传递和日志停止之前停止，因此它排空的执行实例仍可提交和发送消息。恢复的执行实例在
/// Grain 的 <c>OnActivateAsync</c> 完成后启动；运行中的执行实例会在其
/// <c>OnDeactivateAsync</c> 运行之前收到中止通知；参见 <see cref="DurableTaskActivationObserver"/>。
/// </para>
/// <para>
/// 任务状态存储在 Grain 的日志中，这是 Grain 执行实例的权威数据来源，因此激活
/// 设置要求 Grain 具有日志化状态管理器及基于日志的任务存储；如果其中任何一项缺失，
/// 激活就会失败。
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
    /// 确保服务为 <paramref name="services"/> 的激活实例将其任务状态存储在日志中。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 激活实例没有日志化状态管理器，或其任务存储不是基于日志的存储。
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
