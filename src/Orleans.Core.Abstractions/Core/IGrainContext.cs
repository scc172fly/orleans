using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime
{
    /// <summary>
    /// 从运行时的角度表示一个 Grain。
    /// </summary>
    public interface IGrainContext : ITargetHolder, IEquatable<IGrainContext>
    {
        /// <summary>
        /// 获取此 Grain 的引用。
        /// </summary>
        GrainReference GrainReference { get; }

        /// <summary>
        /// 获取 Grain 的标识。
        /// </summary>
        GrainId GrainId { get; }

        /// <summary>
        /// 获取 Grain 实例；如果尚未设置该实例，则返回 <see langword="null"/>。
        /// </summary>
        object? GrainInstance { get; }

        /// <summary>
        /// 获取激活实例 ID。
        /// </summary>
        ActivationId ActivationId { get; }

        /// <summary>
        /// 获取激活实例的地址。
        /// </summary>
        GrainAddress Address { get; }

        /// <summary>
        /// 获取 <see cref="IServiceProvider" />，通过它可以访问 Grain 激活实例的服务容器。
        /// </summary>
        IServiceProvider ActivationServices { get; }

        /// <summary>
        /// 获取可观察的 <see cref="Grain"/> 生命周期，可用于添加生命周期钩子。
        /// </summary>
        IGrainLifecycle ObservableLifecycle { get; }

        /// <summary>
        /// 获取调度器。
        /// </summary>
        IWorkItemScheduler Scheduler { get; }

        /// <summary>
        /// 获取一个 <see cref="Task"/>，它在 Grain 停用后完成。
        /// </summary>
        Task Deactivated { get; }

        /// <summary>
        /// 将给定值设置为类型 <typeparamref name="TComponent"/> 对应的组件。
        /// </summary>
        /// <typeparam name="TComponent">用于查找此组件的类型。</typeparam>
        /// <param name="value">组件实例。</param>
        void SetComponent<TComponent>(TComponent? value) where TComponent : class;

        /// <summary>
        /// 向此实例提交一条传入消息。
        /// </summary>
        /// <param name="message">消息。</param>
        /// <remarks>
        /// 调用此方法时，上下文取得消息的所有权，并负责处理
        /// 或转发消息，或者拒绝或丢弃消息并释放其资源，即使激活失败
        /// 或上下文已停用也是如此。
        /// 即使此方法抛出异常，消息的所有权仍归上下文所有。
        /// </remarks>
        void ReceiveMessage(object message);

        /// <summary>
        /// 将 <paramref name="request"/> 作为此实例上的单向请求加入队列。
        /// </summary>
        /// <param name="request">
        /// 要调用的请求。其 <see cref="IInvokable.GetInterfaceType"/> 方法必须返回接口类型。
        /// </param>
        /// <param name="options">请求选项。</param>
        /// <remarks>
        /// <para>
        /// 此请求与实例的其他请求一起调度，并通过 Grain 调用过滤器；过滤器可以观察
        /// <paramref name="request"/>。它会启动一条新的调用链，并且始终在此实例内部执行。
        /// </para>
        /// <para>
        /// 请求及其调用过滤器完成后，实例会将请求结果传递给 <see cref="ILocalRequest.Complete"/>；
        /// 拒绝请求时也会这样做。
        /// </para>
        /// </remarks>
        /// <exception cref="NotSupportedException">此实例不支持本地请求。</exception>
        void ReceiveLocalRequest(ILocalRequest request, LocalRequestOptions options)
            => throw new NotSupportedException($"{GetType()} does not support local requests.");

        /// <summary>
        /// 开始激活此实例。
        /// </summary>
        /// <param name="requestContext">触发此实例激活的请求的上下文（如果存在）。</param>
        /// <param name="cancellationToken">取消令牌；取消时指示此过程应尽快完成。</param>
        /// <remarks>
        /// 创建者提交所有状态恢复工作后，对每个新创建的 Grain 激活实例调用此方法一次。
        /// 此方法将激活工作加入调度。上下文通过请求和生命周期的失败处理路径处理激活失败。
        /// </remarks>
        void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default);

        /// <summary>
        /// 开始停用此实例。
        /// </summary>
        /// <param name="deactivationReason">停用原因，仅供参考。</param>
        /// <param name="cancellationToken">取消令牌；取消时指示此过程应尽快完成。</param>
        void Deactivate(DeactivationReason deactivationReason, CancellationToken cancellationToken = default);

        /// <summary>
        /// 开始根据给定的状态恢复上下文恢复此实例。
        /// </summary>
        /// <remarks>
        /// 此方法调度状态恢复工作，并将 <paramref name="context"/> 的所有权转交给此实例。
        /// 实现应在内部处理操作失败，以便创建者之后仍可调用 <see cref="Activate"/>。
        /// </remarks>
        void Rehydrate(IRehydrationContext context);

        /// <summary>
        /// 开始尝试将此实例迁移到其他位置。
        /// 迁移会捕获当前 <see cref="RequestContext"/>，并将其提供给激活实例的放置决策器，以便选择新位置时参考。
        /// 迁移在没有请求正在执行时异步进行；如果激活实例的放置决策器没有选出其他位置，则不会迁移。
        /// </summary>
        /// <param name="requestContext">请求上下文，会提供给放置决策器，以便它在选择新位置时检查。</param>
        /// <param name="cancellationToken">取消令牌；取消时指示此过程应尽快完成。</param>
        void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// <see cref="IGrainContext"/> 的扩展方法。
    /// </summary>
    public static class GrainContextExtensions
    {
        /// <summary>
        /// 停用给定的 Grain。
        /// </summary>
        /// <param name="grainContext">
        /// Grain 上下文。
        /// </param>
        /// <param name="deactivationReason">
        /// 停用原因。
        /// </param>
        /// <param name="cancellationToken">取消令牌；取消时指示此过程应尽快完成。</param>
        /// <returns>
        /// 一个在 Grain 停用后完成的 <see cref="Task"/>。
        /// </returns>
        [Obsolete("This method is error-prone: waiting deactivation to complete from within the grain being deactivated will usually result in a deadlock.")]
        public static Task DeactivateAsync(this IGrainContext grainContext, DeactivationReason deactivationReason, CancellationToken cancellationToken = default)
        {
            grainContext.Deactivate(deactivationReason, cancellationToken);
            return grainContext.Deactivated;
        }
    }

    /// <summary>
    /// 提供在 Grain 上调度任务的功能。
    /// </summary>
    public interface IWorkItemScheduler
    {
        /// <summary>
        /// 调度一个由此实例执行的操作。
        /// </summary>
        /// <param name="action">
        /// 操作。
        /// </param>
        void QueueAction(Action action);

        /// <summary>
        /// 调度一个由此实例启动的任务。
        /// </summary>
        /// <param name="task">任务。</param>
        void QueueTask(Task task);

        /// <summary>
        /// 调度一个由此实例执行的工作项。
        /// </summary>
        /// <param name="action">工作项。</param>
        /// <param name="state">调用工作项时传入的状态。</param>
        void QueueAction(Action<object> action, object state);
    }

    /// <summary>
    /// 提供对当前正在执行的 Grain 上下文的访问。
    /// </summary>
    public interface IGrainContextAccessor
    {
        /// <summary>
        /// 获取当前正在执行的 Grain 上下文。
        /// </summary>
        IGrainContext GrainContext { get; }
    }

    /// <summary>
    /// 提供访问或安装 Grain 扩展的功能。
    /// </summary>
    public interface IGrainExtensionBinder
    {
        /// <summary>
        /// 返回为给定 <typeparamref name="TExtensionInterface"/> 注册的 Grain 扩展。
        /// </summary>
        /// <typeparam name="TExtensionInterface">
        /// Grain 扩展的接口。
        /// </typeparam>
        /// <returns>
        /// 绑定到此 Grain 的扩展实现。
        /// </returns>
        TExtensionInterface GetExtension<TExtensionInterface>() where TExtensionInterface : class, IGrainExtension;

        /// <summary>
        /// 如果尚未绑定，则将扩展绑定到可寻址对象。
        /// </summary>
        /// <typeparam name="TExtension">扩展的类型（例如 StreamConsumerExtension）。</typeparam>
        /// <typeparam name="TExtensionInterface">实现的公共接口类型。</typeparam>
        /// <param name="newExtensionFunc">用于创建新扩展对象的工厂函数。</param>
        /// <returns>元组：第一项是扩展，第二项是指向该扩展接口的可寻址引用。</returns>
        (TExtension, TExtensionInterface) GetOrSetExtension<TExtension, TExtensionInterface>(Func<TExtension> newExtensionFunc)
            where TExtension : class, TExtensionInterface
            where TExtensionInterface : class, IGrainExtension;
    }
}
