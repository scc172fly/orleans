using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime
{
    /// <summary>
    /// 从运行时间的角度代表 grain 。
    /// </summary>
    public interface IGrainContext : ITargetHolder, IEquatable<IGrainContext>
    {
        /// <summary>
        /// 获取此 grain的引用。
        /// </summary>
        GrainReference GrainReference { get; }

        /// <summary>
        /// 获取 grain 身份。
        /// </summary>
        GrainId GrainId { get; }

        /// <summary>
        /// 得到 grain 实例,或 <see langword="null"/> 如果 grain 实例尚未设定 。
        /// </summary>
        object? GrainInstance { get; }

        /// <summary>
        /// 获得激活代号。
        /// </summary>
        ActivationId ActivationId { get; }

        /// <summary>
        /// 获得激活地址。
        /// </summary>
        GrainAddress Address { get; }

        /// <summary>
        /// 得到 <see cref="IServiceProvider" /> 提供获取 grain 启动服务集装箱
        /// </summary>
        IServiceProvider ActivationServices { get; }

        /// <summary>
        /// 获得可观测到的 <see cref="Grain"/> 生命周期,该生命周期可用于添加生命周期钩。
        /// </summary>
        IGrainLifecycle ObservableLifecycle { get; }

        /// <summary>
        /// 去拿调度器
        /// </summary>
        IWorkItemScheduler Scheduler { get; }

        /// <summary>
        /// 得到 <see cref="Task"/> 于 grain 已经解除了作用。
        /// </summary>
        Task Deactivated { get; }

        /// <summary>
        /// 将提供值设为类型 <typeparamref name="TComponent"/>的组件。
        /// </summary>
        /// <typeparam name="TComponent">用于查找此组件的类型。</typeparam>
        /// <param name="value">组件实例。</param>
        void SetComponent<TComponent>(TComponent? value) where TComponent : class;

        /// <summary>
        /// 向此实例提交收到的信息 。
        /// </summary>
        /// <param name="message">电文。</param>
        /// <remarks>
        /// 当使用该方法时,上下文将承担电文的所有权。
        /// 或传递信息,或拒绝或抛弃信息并释放其资源,包括启动失败时
        /// 或上下文被禁用。
        /// 如果采用这种方法,所有权仍与上下文相关。
        /// </remarks>
        void ReceiveMessage(object message);

        /// <summary>
        /// 在此情况下将 <paramref name="request"/> 排为单向请求。
        /// </summary>
        /// <param name="request">
        /// 请求引用。 它的 <see cref="IInvokable.GetInterfaceType"/> 方法必须返回接口类型 。
        /// </param>
        /// <param name="options">请求选项。</param>
        /// <remarks>
        /// <para>
        /// 请求与案件的其他请求一起安排,通过 grain 呼叫过滤器,以观察
        /// <paramref name="request"/>. 它启动一个新的呼叫链, 并且永远不会离开这个例子。
        /// </para>
        /// <para>
        /// 实例在请求完成后将请求结果传送到 <see cref="ILocalRequest.Complete"/> ,其调用过滤器完成后
        /// 或拒绝请求。
        /// </para>
        /// </remarks>
        /// <exception cref="NotSupportedException">此例不支持本地请求 。</exception>
        void ReceiveLocalRequest(ILocalRequest request, LocalRequestOptions options)
            => throw new NotSupportedException($"{GetType()} does not support local requests.");

        /// <summary>
        /// 启动这个实例
        /// </summary>
        /// <param name="requestContext">请求中要求启动这一审判的背景(如果有的话)。</param>
        /// <param name="cancellationToken">取消符,该标记在取消时表示进程应迅速完成。</param>
        /// <remarks>
        /// 创建者在提交任何补液工作后,对每个新创建的 grain 激活程序都使用这种方法一次。
        /// 方法计划激活。 上下文通过请求和生命周期故障路径处理启动失败。
        /// </remarks>
        void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default);

        /// <summary>
        /// 开始取消此实例的激活 。
        /// </summary>
        /// <param name="deactivationReason">为提供信息的目的,说明解除其作用的理由。</param>
        /// <param name="cancellationToken">取消符,该标记在取消时表示进程应迅速完成。</param>
        void Deactivate(DeactivationReason deactivationReason, CancellationToken cancellationToken = default);

        /// <summary>
        /// 开始从提供的补液环境中再补充这一实例。
        /// </summary>
        /// <remarks>
        /// 这种方法安排补液和将 <paramref name="context"/> 的所有权转让给本例。
        /// 实施程序处理内部操作故障,以便创建者随后可以调用 <see cref="Activate"/>。
        /// </remarks>
        void Rehydrate(IRehydrationContext context);

        /// <summary>
        /// 开始尝试将这个实例移到另一个地方。
        /// 移民捕捉到当前的 <see cref="RequestContext"/>,向启动的安置主任提供,以便其在选择新地点时能够加以考虑。
        /// 移民将不同步地发生,而没有执行任何请求;如果启动的安置主任不选择其他地点,则不会出现移徙。
        /// </summary>
        /// <param name="requestContext">向安置主任提供请求的背景,以便在选择新地点时加以审查。</param>
        /// <param name="cancellationToken">取消符,该标记在取消时表示进程应迅速完成。</param>
        void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// <see cref="IGrainContext"/>的扩展期。
    /// </summary>
    public static class GrainContextExtensions
    {
        /// <summary>
        /// 取消提供 grain。
        /// </summary>
        /// <param name="grainContext">
        /// grain 上下文。
        /// </param>
        /// <param name="deactivationReason">
        /// 停止功能的原因
        /// </param>
        /// <param name="cancellationToken">取消标记, 当取消时表示该过程应迅速完成。</param>
        /// <returns>
        /// A <see cref="Task"/> 完成后, grain 已经解除了作用。
        /// </returns>
        [Obsolete("This method is error-prone: waiting deactivation to complete from within the grain being deactivated will usually result in a deadlock.")]
        public static Task DeactivateAsync(this IGrainContext grainContext, DeactivationReason deactivationReason, CancellationToken cancellationToken = default)
        {
            grainContext.Deactivate(deactivationReason, cancellationToken);
            return grainContext.Deactivated;
        }
    }

    /// <summary>
    /// 是否具有安排 grain任务的职能。
    /// </summary>
    public interface IWorkItemScheduler
    {
        /// <summary>
        /// 将执行该例的行动排成时间表。
        /// </summary>
        /// <param name="action">
        /// 动作
        /// </param>
        void QueueAction(Action action);

        /// <summary>
        /// 计划此实例要启动的任务。
        /// </summary>
        /// <param name="task">任务</param>
        void QueueTask(Task task);

        /// <summary>
        /// 将执行此例的工作项目排入时间表。
        /// </summary>
        /// <param name="action">工作项目。</param>
        /// <param name="state">国家援引项目时通过。</param>
        void QueueAction(Action<object> action, object state);
    }

    /// <summary>
    /// 提供当前执行的 grain 上下文的访问权限。
    /// </summary>
    public interface IGrainContextAccessor
    {
        /// <summary>
        /// 获取当前执行的 grain 上下文。
        /// </summary>
        IGrainContext GrainContext { get; }
    }

    /// <summary>
    /// 获取或安装 grain扩展的功能。
    /// </summary>
    public interface IGrainExtensionBinder
    {
        /// <summary>
        /// 返回 grain 所提供文件的登记延期 <typeparamref name="TExtensionInterface"/>.
        /// </summary>
        /// <typeparam name="TExtensionInterface">
        /// grain 扩展界面。
        /// </typeparam>
        /// <returns>
        /// 执行与本 grain有关的延长期。
        /// </returns>
        TExtensionInterface GetExtension<TExtensionInterface>() where TExtensionInterface : class, IGrainExtension;

        /// <summary>
        /// 将扩展部分绑在可标定对象的扩展面上,如果尚未完成的话。
        /// </summary>
        /// <typeparam name="TExtension">扩展名类型(e.g. StreamConsumerExtension).</typeparam>
        /// <typeparam name="TExtensionInterface">执行的公共接口类型。</typeparam>
        /// <param name="newExtensionFunc">制造新扩展对象的工厂函数。</param>
        /// <returns>象牙,包含第一个扩展名,第二个可处理的扩展名界面引用。</returns>
        (TExtension, TExtensionInterface) GetOrSetExtension<TExtension, TExtensionInterface>(Func<TExtension> newExtensionFunc)
            where TExtension : class, TExtensionInterface
            where TExtensionInterface : class, IGrainExtension;
    }
}
