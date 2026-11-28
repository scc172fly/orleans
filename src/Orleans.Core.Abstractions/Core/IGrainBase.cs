using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;
using Orleans.Timers;

namespace Orleans
{
    /// <summary>
    /// Grain 实现的接口。
    /// </summary>
    public interface IGrainBase
    {
        /// <summary>
        /// 获取 Grain 上下文。
        /// </summary>
        IGrainContext GrainContext { get; }

        /// <summary>
        /// Grain 实现可重写此方法以处理激活。
        /// </summary>
        /// <param name="token">取消令牌，用于指示激活过程应尽快中止。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task OnActivateAsync(CancellationToken token) => Task.CompletedTask;

        /// <summary>
        /// Grain 实现可重写此方法以处理停用。
        /// </summary>
        /// <param name="reason">停用原因。</param>
        /// <param name="token">取消令牌，用于指示停用过程应尽快完成。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task OnDeactivateAsync(DeactivationReason reason, CancellationToken token) => Task.CompletedTask;
    }

    /// <summary>
    /// <see cref="IGrainBase"/> 实现的辅助方法。
    /// </summary>
    public static class GrainBaseExtensions
    {
        /// <summary>
        /// 当前 Grain 方法调用完成后，停用此 Grain 激活实例。
        /// 此调用将标记当前 Grain 的此激活实例，使其在当前方法结束后被停用并移除。
        /// 下一次调用此 Grain 时将使用另一个激活实例，通常意味着由运行时自动创建新的激活实例。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 为 <see langword="null"/>。</exception>
        public static void DeactivateOnIdle(this IGrainBase grain)
        {
            ArgumentNullException.ThrowIfNull(grain);
            grain.GrainContext.Deactivate(new(DeactivationReasonCode.ApplicationRequested, $"{nameof(DeactivateOnIdle)} was called."));
        }

        /// <summary>
        /// 开始尝试将此实例迁移到另一个位置。
        /// 迁移会捕获当前 <see cref="RequestContext"/>，并将其提供给此激活实例的放置决策器，以便选择新位置时加以考虑。
        /// 迁移将在没有正在执行的请求时异步进行；如果此激活实例的放置决策器未选出其他位置，则不会迁移。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 为 <see langword="null"/>。</exception>
        public static void MigrateOnIdle(this IGrainBase grain)
        {
            ArgumentNullException.ThrowIfNull(grain);
            grain.GrainContext.Migrate(RequestContext.CallContextData?.Value.Values);
        }

        /// <summary>
        /// 将 <paramref name="callback"/> 作为请求在此 Grain 激活实例上运行。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">要运行的回调。</param>
        /// <param name="cancellationToken">
        /// 请求仍在排队时，取消令牌可取消请求。一旦回调开始执行，令牌便会传给回调。
        /// </param>
        /// <returns>一个根据回调结果完成的 <see cref="Task"/>。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        public static Task RunRequestAsync(this IGrainBase grain, Func<CancellationToken, Task> callback, CancellationToken cancellationToken = default) =>
            RunRequestAsync(grain, callback, default(LocalRequestOptions), cancellationToken);

        /// <summary>
        /// 将 <paramref name="callback"/> 作为请求在此 Grain 激活实例上运行。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">要运行的回调。</param>
        /// <param name="options">请求选项。</param>
        /// <param name="cancellationToken">
        /// 请求仍在排队时，取消令牌可取消请求。一旦回调开始执行，令牌便会传给回调。
        /// </param>
        /// <returns>一个根据回调结果完成的 <see cref="Task"/>。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 或 <paramref name="callback"/> 为 <see langword="null"/>。</exception>
        public static Task RunRequestAsync(this IGrainBase grain, Func<CancellationToken, Task> callback, LocalRequestOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(grain);
            return RunRequestCore<Func<CancellationToken, Task>, object?>(
                grain.GrainContext,
                static (callback, cancellationToken) => callback(cancellationToken),
                callback,
                options,
                cancellationToken);
        }

        /// <summary>
        /// 将 <paramref name="callback"/> 作为请求在此 Grain 激活实例上运行。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">要运行的回调。</param>
        /// <param name="state">传递给回调的状态。</param>
        /// <param name="cancellationToken">
        /// 请求仍在排队时，取消令牌可取消请求。一旦回调开始执行，令牌便会传给回调。
        /// </param>
        /// <typeparam name="TState"><paramref name="state"/> 参数的类型。</typeparam>
        /// <returns>一个根据回调结果完成的 <see cref="Task"/>。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        public static Task RunRequestAsync<TState>(this IGrainBase grain, Func<TState, CancellationToken, Task> callback, TState state, CancellationToken cancellationToken = default) =>
            RunRequestAsync(grain, callback, state, default(LocalRequestOptions), cancellationToken);

        /// <summary>
        /// 将 <paramref name="callback"/> 作为请求在此 Grain 激活实例上运行。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">要运行的回调。</param>
        /// <param name="state">传递给回调的状态。</param>
        /// <param name="options">请求选项。</param>
        /// <param name="cancellationToken">
        /// 请求仍在排队时，取消令牌可取消请求。一旦回调开始执行，令牌便会传给回调。
        /// </param>
        /// <typeparam name="TState"><paramref name="state"/> 参数的类型。</typeparam>
        /// <returns>一个根据回调结果完成的 <see cref="Task"/>。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 或 <paramref name="callback"/> 为 <see langword="null"/>。</exception>
        public static Task RunRequestAsync<TState>(this IGrainBase grain, Func<TState, CancellationToken, Task> callback, TState state, LocalRequestOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(grain);
            return RunRequestCore<TState, object?>(grain.GrainContext, callback, state, options, cancellationToken);
        }

        /// <summary>
        /// 将 <paramref name="callback"/> 作为请求在此 Grain 激活实例上运行。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">要运行的回调。</param>
        /// <param name="cancellationToken">
        /// 请求仍在排队时，取消令牌可取消请求。一旦回调开始执行，令牌便会传给回调。
        /// </param>
        /// <returns>一个根据回调结果完成的 <see cref="Task"/>。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        public static Task RunRequestAsync(this IGrainBase grain, Action<CancellationToken> callback, CancellationToken cancellationToken = default) =>
            RunRequestAsync(grain, callback, default(LocalRequestOptions), cancellationToken);

        /// <summary>
        /// 将 <paramref name="callback"/> 作为请求在此 Grain 激活实例上运行。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">要运行的回调。</param>
        /// <param name="options">请求选项。</param>
        /// <param name="cancellationToken">
        /// 请求仍在排队时，取消令牌可取消请求。一旦回调开始执行，令牌便会传给回调。
        /// </param>
        /// <returns>一个根据回调结果完成的 <see cref="Task"/>。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 或 <paramref name="callback"/> 为 <see langword="null"/>。</exception>
        public static Task RunRequestAsync(this IGrainBase grain, Action<CancellationToken> callback, LocalRequestOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(grain);
            return RunRequestCore<Action<CancellationToken>, object?>(
                grain.GrainContext,
                static (callback, cancellationToken) =>
                {
                    callback(cancellationToken);
                    return Task.CompletedTask;
                },
                callback,
                options,
                cancellationToken);
        }

        /// <summary>
        /// 将 <paramref name="callback"/> 作为请求在此 Grain 激活实例上运行。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">要运行的回调。</param>
        /// <param name="state">传递给回调的状态。</param>
        /// <param name="cancellationToken">
        /// 请求仍在排队时，取消令牌可取消请求。一旦回调开始执行，令牌便会传给回调。
        /// </param>
        /// <typeparam name="TState"><paramref name="state"/> 参数的类型。</typeparam>
        /// <returns>一个根据回调结果完成的 <see cref="Task"/>。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        public static Task RunRequestAsync<TState>(this IGrainBase grain, Action<TState, CancellationToken> callback, TState state, CancellationToken cancellationToken = default) =>
            RunRequestAsync(grain, callback, state, default(LocalRequestOptions), cancellationToken);

        /// <summary>
        /// 将 <paramref name="callback"/> 作为请求在此 Grain 激活实例上运行。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">要运行的回调。</param>
        /// <param name="state">传递给回调的状态。</param>
        /// <param name="options">请求选项。</param>
        /// <param name="cancellationToken">
        /// 请求仍在排队时，取消令牌可取消请求。一旦回调开始执行，令牌便会传给回调。
        /// </param>
        /// <typeparam name="TState"><paramref name="state"/> 参数的类型。</typeparam>
        /// <returns>一个根据回调结果完成的 <see cref="Task"/>。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 或 <paramref name="callback"/> 为 <see langword="null"/>。</exception>
        public static Task RunRequestAsync<TState>(this IGrainBase grain, Action<TState, CancellationToken> callback, TState state, LocalRequestOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(grain);
            return RunRequestCore<(Action<TState, CancellationToken> Callback, TState State), object?>(
                grain.GrainContext,
                static (state, cancellationToken) =>
                {
                    state.Callback(state.State, cancellationToken);
                    return Task.CompletedTask;
                },
                (callback, state),
                options,
                cancellationToken);
        }

        /// <summary>
        /// 将 <paramref name="callback"/> 作为请求在此 Grain 激活实例上运行。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">要运行的回调。</param>
        /// <param name="cancellationToken">
        /// 请求仍在排队时，取消令牌可取消请求。一旦回调开始执行，令牌便会传给回调。
        /// </param>
        /// <typeparam name="TResult">回调结果的类型。</typeparam>
        /// <returns>一个根据回调结果完成的 <see cref="Task{TResult}"/>。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        public static Task<TResult> RunRequestAsync<TResult>(this IGrainBase grain, Func<CancellationToken, Task<TResult>> callback, CancellationToken cancellationToken = default) =>
            RunRequestAsync(grain, callback, default(LocalRequestOptions), cancellationToken);

        /// <summary>
        /// 将 <paramref name="callback"/> 作为请求在此 Grain 激活实例上运行。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">要运行的回调。</param>
        /// <param name="options">请求选项。</param>
        /// <param name="cancellationToken">
        /// 请求仍在排队时，取消令牌可取消请求。一旦回调开始执行，令牌便会传给回调。
        /// </param>
        /// <typeparam name="TResult">回调结果的类型。</typeparam>
        /// <returns>一个根据回调结果完成的 <see cref="Task{TResult}"/>。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 或 <paramref name="callback"/> 为 <see langword="null"/>。</exception>
        public static Task<TResult> RunRequestAsync<TResult>(this IGrainBase grain, Func<CancellationToken, Task<TResult>> callback, LocalRequestOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(grain);
            return RunRequestCore<Func<CancellationToken, Task<TResult>>, TResult>(
                grain.GrainContext,
                static (callback, cancellationToken) => callback(cancellationToken),
                callback,
                options,
                cancellationToken);
        }

        /// <summary>
        /// 将 <paramref name="callback"/> 作为请求在此 Grain 激活实例上运行。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">要运行的回调。</param>
        /// <param name="state">传递给回调的状态。</param>
        /// <param name="cancellationToken">
        /// 请求仍在排队时，取消令牌可取消请求。一旦回调开始执行，令牌便会传给回调。
        /// </param>
        /// <typeparam name="TState"><paramref name="state"/> 参数的类型。</typeparam>
        /// <typeparam name="TResult">回调结果的类型。</typeparam>
        /// <returns>一个根据回调结果完成的 <see cref="Task{TResult}"/>。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        public static Task<TResult> RunRequestAsync<TState, TResult>(this IGrainBase grain, Func<TState, CancellationToken, Task<TResult>> callback, TState state, CancellationToken cancellationToken = default) =>
            RunRequestAsync(grain, callback, state, default(LocalRequestOptions), cancellationToken);

        /// <summary>
        /// 将 <paramref name="callback"/> 作为请求在此 Grain 激活实例上运行。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">要运行的回调。</param>
        /// <param name="state">传递给回调的状态。</param>
        /// <param name="options">请求选项。</param>
        /// <param name="cancellationToken">
        /// 请求仍在排队时，取消令牌可取消请求。一旦回调开始执行，令牌便会传给回调。
        /// </param>
        /// <typeparam name="TState"><paramref name="state"/> 参数的类型。</typeparam>
        /// <typeparam name="TResult">回调结果的类型。</typeparam>
        /// <returns>一个根据回调结果完成的 <see cref="Task{TResult}"/>。</returns>
        /// <remarks>
        /// <para>
        /// 该请求与激活实例的其他请求一起调度。默认遵循 Grain 的可重入性规则：
        /// 在非可重入 Grain 上，回调独占执行，行为与 Grain 方法调用相同。
        /// 请求以 <c>IGrainRequestInvoker.RunRequestAsync</c> 的形式通过 Grain 调用过滤器。
        /// 它会启动一条新的调用链，且默认保持激活实例存活。
        /// 如果调用过滤器执行失败，或在未调用请求的情况下设置结果，则返回的任务以该结果完成。
        /// 回调的状态和结果按引用传递，而不是复制。
        /// </para>
        /// <para>
        /// 使用此方法将后台任务（例如流处理器或消息处理器）中的代码作为 Grain 请求运行。
        /// 可从任意线程调用此方法。对于非可重入 Grain，在同一激活实例的请求内部等待返回的任务会导致死锁；
        /// 在回调内部等待对同一激活实例的调用也会导致死锁。
        /// </para>
        /// <para>
        /// 如果请求开始之前激活实例已停用，则返回的任务将以
        /// <see cref="OrleansMessageRejectionException"/> 异常结束。
        /// 若需要向调用过滤器和跟踪系统呈现不同的接口名、方法名或参数，
        /// 将自定义 <see cref="ILocalRequest"/> 通过 <see cref="IGrainContext.ReceiveLocalRequest"/> 加入队列。
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 或 <paramref name="callback"/> 为 <see langword="null"/>。</exception>
        public static Task<TResult> RunRequestAsync<TState, TResult>(this IGrainBase grain, Func<TState, CancellationToken, Task<TResult>> callback, TState state, LocalRequestOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(grain);
            return RunRequestCore<TState, TResult>(grain.GrainContext, callback, state, options, cancellationToken);
        }

        private static Task<TResult> RunRequestCore<TState, TResult>(
            IGrainContext grainContext,
            Func<TState, CancellationToken, Task> callback,
            TState state,
            LocalRequestOptions options,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<TResult>(cancellationToken);
            }

            // 请求加入队列后，其所有权归激活实例；实例在调用完成后释放请求。
#pragma warning disable CA2000 // Dispose objects before losing scope
            var request = new GrainRequest<TState, TResult>(callback, state, cancellationToken);
#pragma warning restore CA2000 // Dispose objects before losing scope
            try
            {
                grainContext.ReceiveLocalRequest(request, options);
            }
            catch (Exception exception)
            {
                // 完成请求会释放其取消注册，并通过返回的任务报告失败。
                ((ILocalRequest)request).Complete(Response.FromException(exception));
            }

            return request.Task;
        }

        /// <summary>
        /// 创建 Grain 定时器。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">定时器回调，每当定时器到期时调用。</param>
        /// <param name="state">传递给回调的状态。</param>
        /// <param name="options">
        /// 创建定时器的选项。
        /// </param>
        /// <typeparam name="TState"><paramref name="state"/> 参数的类型。</typeparam>
        /// <returns>
        /// 表示定时器的 <see cref="IGrainTimer"/> 实例。
        /// </returns>
        /// <remarks>
        /// <para>
        /// Grain 定时器默认不会保持 Grain 处于激活状态。将 <see cref="GrainTimerCreationOptions.KeepAlive"/> 设置为
        /// <see langword="true"/> 后，每次定时器触发都会延长 Grain 激活实例的生命周期。
        /// 如果定时器触发不频繁，Grain 仍可能因空闲而停用。
        /// Grain 停用时，所有活动定时器都会被丢弃。
        /// </para>
        /// <para>
        /// 在回调返回的 <see cref="Task"/> 完成之前，不会调度下一次定时器触发。
        /// 也就是说，同一定时器的回调绝不会与自身并发执行。
        /// 如果将 <see cref="GrainTimerCreationOptions.Interleave"/> 设置为 <see langword="true"/>，则允许定时器回调
        /// 与其他 Grain 方法调用及其他定时器交错执行。
        /// 如果将 <see cref="GrainTimerCreationOptions.Interleave"/> 设置为 <see langword="false"/>，则定时器回调会遵循
        /// Grain 的可重入性设置，与普通 Grain 方法调用一样。
        /// </para>
        /// <para>
        /// 可以随时调用 <see cref="IGrainTimer"/> 的 <see cref="IDisposable.Dispose"/> 方法停止定时器。
        /// 释放定时器后，不会再调度新的定时器触发。
        /// </para>
        /// <para>
        /// 可以调用定时器的 <see cref="IGrainTimer.Change(TimeSpan, TimeSpan)"/> 方法更新其首次触发时间和周期。
        /// 每次更新定时器后，都会根据更新后的首次触发时间调度下一次触发。
        /// 随后的触发会在更新后的周期结束后进行调度。
        /// 请注意，此行为与 <see cref="Timer.Change(TimeSpan, TimeSpan)"/> 方法相同。
        /// </para>
        /// <para>
        /// 回调抛出的异常会被记录，但不会阻止下一次定时器触发加入队列。
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 或 <paramref name="callback"/> 为 <see langword="null"/>。</exception>
        public static IGrainTimer RegisterGrainTimer<TState>(this IGrainBase grain, Func<TState, CancellationToken, Task> callback, TState state, GrainTimerCreationOptions options)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(grain);
            if (grain is Grain grainClass)
            {
                grainClass.EnsureRuntime();
                return grainClass.Runtime.TimerRegistry.RegisterGrainTimer(grainClass.GrainContext, callback, state, options);
            }

            return grain.GrainContext.ActivationServices.GetRequiredService<ITimerRegistry>().RegisterGrainTimer(grain.GrainContext, callback, state, options);
        }

        /// <summary>
        /// 创建 Grain 定时器。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">定时器回调，每当定时器到期时调用。</param>
        /// <param name="options">
        /// 创建定时器的选项。
        /// </param>
        /// <returns>
        /// 表示定时器的 <see cref="IGrainTimer"/> 实例。
        /// </returns>
        /// <remarks>
        /// <para>
        /// Grain 定时器默认不会保持 Grain 处于激活状态。将 <see cref="GrainTimerCreationOptions.KeepAlive"/> 设置为
        /// <see langword="true"/> 后，每次定时器触发都会延长 Grain 激活实例的生命周期。
        /// 如果定时器触发不频繁，Grain 仍可能因空闲而停用。
        /// Grain 停用时，所有活动定时器都会被丢弃。
        /// </para>
        /// <para>
        /// 在回调返回的 <see cref="Task"/> 完成之前，不会调度下一次定时器触发。
        /// 也就是说，同一定时器的回调绝不会与自身并发执行。
        /// 如果将 <see cref="GrainTimerCreationOptions.Interleave"/> 设置为 <see langword="true"/>，则允许定时器回调
        /// 与其他 Grain 方法调用及其他定时器交错执行。
        /// 如果将 <see cref="GrainTimerCreationOptions.Interleave"/> 设置为 <see langword="false"/>，则定时器回调会遵循
        /// Grain 的可重入性设置，与普通 Grain 方法调用一样。
        /// </para>
        /// <para>
        /// 可以随时调用 <see cref="IGrainTimer"/> 的 <see cref="IDisposable.Dispose"/> 方法停止定时器。
        /// 释放定时器后，不会再调度新的定时器触发。
        /// </para>
        /// <para>
        /// 可以调用定时器的 <see cref="IGrainTimer.Change(TimeSpan, TimeSpan)"/> 方法更新其首次触发时间和周期。
        /// 每次更新定时器后，都会根据更新后的首次触发时间调度下一次触发。
        /// 随后的触发会在更新后的周期结束后进行调度。
        /// 请注意，此行为与 <see cref="Timer.Change(TimeSpan, TimeSpan)"/> 方法相同。
        /// </para>
        /// <para>
        /// 回调抛出的异常会被记录，但不会阻止下一次定时器触发加入队列。
        /// </para>
        /// </remarks>
        public static IGrainTimer RegisterGrainTimer(this IGrainBase grain, Func<CancellationToken, Task> callback, GrainTimerCreationOptions options)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return RegisterGrainTimer(grain, static (callback, cancellationToken) => callback(cancellationToken), callback, options);
        }

        /// <summary>
        /// 创建 Grain 定时器。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">定时器回调，每当定时器到期时调用。</param>
        /// <param name="options">用于创建定时器的选项。</param>
        /// <returns>表示定时器的 <see cref="IGrainTimer"/> 实例。</returns>
        public static IGrainTimer RegisterGrainTimer(this IGrainBase grain, Func<Task> callback, GrainTimerCreationOptions options)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return RegisterGrainTimer(grain, static (callback, cancellationToken) => callback(), callback, options);
        }

        /// <inheritdoc cref="RegisterGrainTimer(IGrainBase, Func{Task}, GrainTimerCreationOptions)"/>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">定时器回调，每当定时器到期时调用。</param>
        /// <param name="state">传递给回调的状态。</param>
        /// <param name="options">用于创建定时器的选项。</param>
        /// <typeparam name="TState"><paramref name="state"/> 参数的类型。</typeparam>
        public static IGrainTimer RegisterGrainTimer<TState>(this IGrainBase grain, Func<TState, Task> callback, TState state, GrainTimerCreationOptions options)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return RegisterGrainTimer(grain, static (state, _) => state.Callback(state.State), (Callback: callback, State: state), options);
        }

        /// <summary>
        /// 创建 Grain 定时器。
        /// </summary>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">定时器回调，每当定时器到期时调用。</param>
        /// <param name="dueTime">
        /// 一个 <see cref="TimeSpan"/>，表示调用创建 <see cref="IGrainTimer"/> 时指定的回调方法之前需要等待的时长。
        /// 指定 <see cref="Timeout.InfiniteTimeSpan"/> 可阻止定时器启动。
        /// 指定 <see cref="TimeSpan.Zero"/> 可立即启动定时器。
        /// </param>
        /// <param name="period">
        /// 创建 <see cref="IGrainTimer"/> 时指定的回调方法的调用时间间隔。
        /// 指定 <see cref="Timeout.InfiniteTimeSpan "/> 可禁用周期性触发。
        /// </param>
        /// <returns>
        /// 表示定时器的 <see cref="IGrainTimer"/> 实例。
        /// </returns>
        /// <remarks>
        /// <para>
        /// Grain 定时器默认不会保持 Grain 处于激活状态。将 <see cref="GrainTimerCreationOptions.KeepAlive"/> 设置为
        /// <see langword="true"/> 后，每次定时器触发都会延长 Grain 激活实例的生命周期。
        /// 如果定时器触发不频繁，Grain 仍可能因空闲而停用。
        /// Grain 停用时，所有活动定时器都会被丢弃。
        /// </para>
        /// <para>
        /// 在回调返回的 <see cref="Task"/> 完成之前，不会调度下一次定时器触发。
        /// 也就是说，同一定时器的回调绝不会与自身并发执行。
        /// 如果将 <see cref="GrainTimerCreationOptions.Interleave"/> 设置为 <see langword="true"/>，则允许定时器回调
        /// 与其他 Grain 方法调用及其他定时器交错执行。
        /// 如果将 <see cref="GrainTimerCreationOptions.Interleave"/> 设置为 <see langword="false"/>，则定时器回调会遵循
        /// Grain 的可重入性设置，与普通 Grain 方法调用一样。
        /// </para>
        /// <para>
        /// 可以随时调用 <see cref="IGrainTimer"/> 的 <see cref="IDisposable.Dispose"/> 方法停止定时器。
        /// 释放定时器后，不会再调度新的定时器触发。
        /// </para>
        /// <para>
        /// 可以调用定时器的 <see cref="IGrainTimer.Change(TimeSpan, TimeSpan)"/> 方法更新其首次触发时间和周期。
        /// 每次更新定时器后，都会根据更新后的首次触发时间调度下一次触发。
        /// 随后的触发会在更新后的周期结束后进行调度。
        /// 请注意，此行为与 <see cref="Timer.Change(TimeSpan, TimeSpan)"/> 方法相同。
        /// </para>
        /// <para>
        /// 回调抛出的异常会被记录，但不会阻止下一次定时器触发加入队列。
        /// </para>
        /// </remarks>
        public static IGrainTimer RegisterGrainTimer(this IGrainBase grain, Func<Task> callback, TimeSpan dueTime, TimeSpan period)
            => RegisterGrainTimer(grain, callback, new() { DueTime = dueTime, Period = period });

        /// <inheritdoc cref="RegisterGrainTimer(IGrainBase, Func{Task}, TimeSpan, TimeSpan)"/>
        public static IGrainTimer RegisterGrainTimer(this IGrainBase grain, Func<CancellationToken, Task> callback, TimeSpan dueTime, TimeSpan period)
            => RegisterGrainTimer(grain, callback, new() { DueTime = dueTime, Period = period });

        /// <inheritdoc cref="RegisterGrainTimer(IGrainBase, Func{Task}, TimeSpan, TimeSpan)"/>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">定时器回调，每当定时器到期时调用。</param>
        /// <param name="state">传递给回调的状态。</param>
        /// <param name="dueTime">调用回调之前的延迟时间。</param>
        /// <param name="period">回调调用之间的时间间隔。</param>
        /// <typeparam name="TState"><paramref name="state"/> 参数的类型。</typeparam>
        public static IGrainTimer RegisterGrainTimer<TState>(this IGrainBase grain, Func<TState, Task> callback, TState state, TimeSpan dueTime, TimeSpan period)
            => RegisterGrainTimer(grain, callback, state, new() { DueTime = dueTime, Period = period });

        /// <inheritdoc cref="RegisterGrainTimer(IGrainBase, Func{Task}, TimeSpan, TimeSpan)"/>
        /// <param name="grain">Grain 实例。</param>
        /// <param name="callback">定时器回调，每当定时器到期时调用。</param>
        /// <param name="state">传递给回调的状态。</param>
        /// <param name="dueTime">调用回调之前的延迟时间。</param>
        /// <param name="period">回调调用之间的时间间隔。</param>
        /// <typeparam name="TState"><paramref name="state"/> 参数的类型。</typeparam>
        public static IGrainTimer RegisterGrainTimer<TState>(this IGrainBase grain, Func<TState, CancellationToken, Task> callback, TState state, TimeSpan dueTime, TimeSpan period)
            => RegisterGrainTimer(grain, callback, state, new() { DueTime = dueTime, Period = period });
    }

    /// <summary>
    /// 用于说明停用原因的原因代码。
    /// </summary>
    [GenerateSerializer]
    public enum DeactivationReasonCode : byte
    {
        /// <summary>
        /// 未提供原因。
        /// </summary>
        None,

        /// <summary>
        /// 进程正在关闭。
        /// </summary>
        ShuttingDown,

        /// <summary>
        /// Grain 激活失败。
        /// </summary>
        ActivationFailed,

        /// <summary>
        /// 此激活实例受到分布式 Grain 目录内部故障的影响。
        /// </summary>
        /// <remarks>
        /// 例如，托管此激活实例对应的 Grain 目录分区的进程发生故障，可能导致这种情况。
        /// </remarks>
        DirectoryFailure,

        /// <summary>
        /// 此激活实例处于空闲状态。
        /// </summary>
        ActivationIdle,

        /// <summary>
        /// 此激活实例未响应命令或请求。
        /// </summary>
        ActivationUnresponsive,

        /// <summary>
        /// 此 Grain 的另一个实例已激活。
        /// </summary>
        DuplicateActivation,

        /// <summary>
        /// 此激活实例收到无法由本地运行进程处理的请求。
        /// </summary>
        IncompatibleRequest,

        /// <summary>
        /// 发生应用程序错误。
        /// </summary>
        ApplicationError,

        /// <summary>
        /// 应用程序请求停用此激活实例。
        /// </summary>
        ApplicationRequested,

        /// <summary>
        /// 此激活实例正在迁移到新位置。
        /// </summary>
        Migrating,

        /// <summary>
        /// 运行时请求停用此激活实例。
        /// </summary>
        RuntimeRequested,

        /// <summary>
        /// 运行时检测到应用程序可用内存不足，因而决定强制停用。
        /// </summary>
        HighMemoryPressure,
    }

}
